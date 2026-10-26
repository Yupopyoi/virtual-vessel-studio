using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using UniGLTF;
using UniGLTF.Extensions.VRMC_vrm;
using UniJSON;
using UniVRM10;
using UniVRM10.Migration;

namespace VirtualVessel.Avatar.Vrm
{
    /// <summary>
    /// Loads VRM 1.0, and VRM 0.x converted to 1.0 while loading, with UniVRM (avatar detailed design
    /// 6.8, 8.2). The only class outside this assembly's internals that other code creates; UniVRM types
    /// never leave this assembly (avatar detailed design 3).
    /// </summary>
    public sealed class VrmAvatarLoader : IAvatarLoader
    {
        public const string FormatName = "vrm";

        private static readonly string[] s_extensions = { ".vrm" };

        public string Format => FormatName;

        public IReadOnlyList<string> Extensions => s_extensions;

        public async Task<IAvatarModel> LoadAsync(string path, CancellationToken cancellationToken)
        {
            byte[] bytes;
            try
            {
                bytes = await Task.Run(() => File.ReadAllBytes(path), cancellationToken);
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
            {
                throw new AvatarLoadException(AvatarProblem.StorageFailed, "The model file could not be read.", exception);
            }

            GltfData data;
            try
            {
                data = await Task.Run(() => GlbLowLevelParser.Parse(path, bytes), cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                throw new AvatarLoadException(AvatarProblem.NotVrm, "The file is not a readable glTF binary.", exception);
            }

            using (data)
            {
                if (!IsVrm(data))
                {
                    throw new AvatarLoadException(AvatarProblem.NotVrm, "The glTF file has no VRM extension.");
                }

                Meta meta10 = null;
                Vrm0Meta meta0 = null;
                Vrm10Instance instance = null;
                try
                {
                    // Hidden until placed, so it never shows for a frame at the origin on the wrong layer.
                    instance = await Vrm10.LoadGltfDataAsync(
                        data,
                        canLoadVrm0X: true,
                        controlRigGenerationOption: ControlRigGenerationOption.Generate,
                        showMeshes: false,
                        vrmMetaInformationCallback: (_, vrm10, vrm0) =>
                        {
                            meta10 = vrm10;
                            meta0 = vrm0;
                        },
                        ct: cancellationToken);
                    if (instance == null)
                    {
                        throw new AvatarLoadException(AvatarProblem.LoadFailed, "UniVRM returned no model.");
                    }

                    return new VrmAvatarModel(instance, ReadInfo(data, meta10, meta0));
                }
                catch (Exception exception)
                {
                    if (instance != null)
                    {
                        UnityObjectDestroyer.DestroyRuntimeOrEditor(instance.gameObject);
                    }

                    if (exception is OperationCanceledException || exception is AvatarLoadException)
                    {
                        throw;
                    }

                    // UniVRM reports every failure as a plain Exception (avatar detailed design 12).
                    throw new AvatarLoadException(AvatarProblem.LoadFailed, "UniVRM could not load the model.", exception);
                }
            }
        }

        private static bool IsVrm(GltfData data)
        {
            if (Vrm10Data.Parse(data) != null)
            {
                return true;
            }

            JsonNode json = data.Json.ParseAsJson();
            return json.TryGet("extensions", out JsonNode extensions) && extensions.ContainsKey("VRM");
        }

        private static AvatarModelInfo ReadInfo(GltfData data, Meta meta10, Vrm0Meta meta0)
        {
            if (meta10 != null)
            {
                return new AvatarModelInfo(meta10.Name, FormatName, "1.0", converted: false, VrmLicense.From(meta10), Thumbnail(data, meta10.ThumbnailImage ?? -1));
            }

            int image = -1;
            if (meta0 != null && meta0.texture >= 0 && data.GLTF.textures != null && meta0.texture < data.GLTF.textures.Count)
            {
                image = data.GLTF.textures[meta0.texture].source ?? -1;
            }

            return new AvatarModelInfo(meta0?.title, FormatName, "0.x", converted: true, VrmLicense.From(meta0), Thumbnail(data, image));
        }

        /// <summary>The image's bytes as stored in the file, or null for none or an unknown image type.</summary>
        private static AvatarThumbnail Thumbnail(GltfData data, int imageIndex)
        {
            if (imageIndex < 0 || data.GLTF.images == null || imageIndex >= data.GLTF.images.Count)
            {
                return null;
            }

            try
            {
                var image = data.GetBytesFromImage(imageIndex);
                if (!image.HasValue)
                {
                    return null;
                }

                // Copied out: the bytes belong to the glTF data, which is disposed after loading.
                string extension = AvatarThumbnail.ExtensionOf(image.Value.mimeType);
                return extension == null ? null : new AvatarThumbnail(image.Value.binary.ToArray(), extension);
            }
            catch (Exception exception) when (!(exception is OutOfMemoryException))
            {
                // A broken thumbnail never stops loading the model; the list shows a placeholder.
                return null;
            }
        }
    }
}
