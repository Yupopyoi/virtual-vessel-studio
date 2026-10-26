using System;
using System.Collections.Generic;
using Mediapipe.Tasks.Components.Containers;
using Mediapipe.Tasks.Vision.FaceLandmarker;
using Mediapipe.Tasks.Vision.HandLandmarker;
using Mediapipe.Tasks.Vision.PoseLandmarker;
using UnityEngine;
using Landmark = Mediapipe.Tasks.Components.Containers.Landmark;
using NormalizedLandmark = Mediapipe.Tasks.Components.Containers.NormalizedLandmark;

namespace VirtualVessel.Tracking.MediaPipe
{
    // The readers copy the plugin's result types into plain arrays for MediaPipeConversion. Each is used
    // by one task's callbacks only, on MediaPipe's threads, and reuses its arrays.

    /// <summary>Pose results as arrays.</summary>
    internal sealed class PoseResultReader
    {
        public Vector3[] World { get; } = new Vector3[MediaPipeConversion.PoseLandmarkCount];

        public Vector2[] Image { get; } = new Vector2[MediaPipeConversion.PoseLandmarkCount];

        public float[] Visibility { get; } = new float[MediaPipeConversion.PoseLandmarkCount];

        /// <summary>Copies the first pose; false when there is none.</summary>
        public bool TryRead(PoseLandmarkerResult result)
        {
            if (result.poseLandmarks == null || result.poseLandmarks.Count == 0 || result.poseWorldLandmarks == null || result.poseWorldLandmarks.Count == 0)
            {
                return false;
            }

            List<NormalizedLandmark> image = result.poseLandmarks[0].landmarks;
            List<Landmark> world = result.poseWorldLandmarks[0].landmarks;
            if (image == null || world == null || image.Count < MediaPipeConversion.PoseLandmarkCount || world.Count < MediaPipeConversion.PoseLandmarkCount)
            {
                return false;
            }

            for (int i = 0; i < MediaPipeConversion.PoseLandmarkCount; i++)
            {
                World[i] = new Vector3(world[i].x, world[i].y, world[i].z);
                Image[i] = new Vector2(image[i].x, image[i].y);
                Visibility[i] = image[i].visibility ?? 1f;
            }

            return true;
        }
    }

    /// <summary>Face results converted into a <see cref="FacesObservation"/>, with the blendshape names resolved once.</summary>
    internal sealed class FaceResultReader
    {
        private readonly bool _sidesMirrored;
        private readonly float[] _shapes = new float[FacePose.ShapeCount];
        private int[] _shapeOf;
        private ulong _supported;

        /// <param name="sidesMirrored">Whether MediaPipe's left and right face values are the image's instead of the person's.</param>
        public FaceResultReader(bool sidesMirrored)
        {
            _sidesMirrored = sidesMirrored;
        }

        public void Read(FaceLandmarkerResult result, double time, FacesObservation into)
        {
            into.Time = time;
            into.Count = 0;
            int count = Math.Min(FacesObservation.Capacity, Math.Min(
                result.faceBlendshapes?.Count ?? 0,
                Math.Min(result.facialTransformationMatrixes?.Count ?? 0, result.faceLandmarks?.Count ?? 0)));
            for (int f = 0; f < count; f++)
            {
                List<Category> categories = result.faceBlendshapes[f].categories;
                List<NormalizedLandmark> landmarks = result.faceLandmarks[f].landmarks;
                if (categories == null || landmarks == null || landmarks.Count <= MediaPipeConversion.FaceNoseTip)
                {
                    continue;
                }

                if (_shapeOf == null || _shapeOf.Length != categories.Count)
                {
                    _shapeOf = MapShapes(categories, _sidesMirrored, out _supported);
                }

                Array.Clear(_shapes, 0, _shapes.Length);
                for (int i = 0; i < categories.Count; i++)
                {
                    if (_shapeOf[i] >= 0)
                    {
                        _shapes[_shapeOf[i]] = categories[i].score;
                    }
                }

                int slot = into.Count++;
                MediaPipeConversion.WriteFace(_shapes, _supported, result.facialTransformationMatrixes[f], time, into.Faces[slot]);
                NormalizedLandmark nose = landmarks[MediaPipeConversion.FaceNoseTip];
                into.Noses[slot] = new Vector2(nose.x, nose.y);
            }
        }

        private static int[] MapShapes(List<Category> categories, bool sidesMirrored, out ulong supported)
        {
            var map = new int[categories.Count];
            supported = 0;
            for (int i = 0; i < categories.Count; i++)
            {
                map[i] = -1;
                if (MediaPipeConversion.TryParseShape(categories[i].categoryName, out FaceShape shape))
                {
                    if (sidesMirrored)
                    {
                        shape = MediaPipeConversion.OtherSide(shape);
                    }

                    map[i] = (int)shape;
                    supported |= 1UL << (int)shape;
                }
            }

            return map;
        }
    }

    /// <summary>Hand results converted into a <see cref="HandsObservation"/>, before their sides are decided.</summary>
    internal sealed class HandResultReader
    {
        private readonly Vector3[] _world = new Vector3[MediaPipeConversion.HandLandmarkCount];
        private readonly Vector2[] _image = new Vector2[MediaPipeConversion.HandLandmarkCount];

        public void Read(HandLandmarkerResult result, double time, HandsObservation into)
        {
            into.Time = time;
            into.Count = 0;
            int count = Math.Min(2, Math.Min(result.handLandmarks?.Count ?? 0, result.handWorldLandmarks?.Count ?? 0));
            for (int h = 0; h < count; h++)
            {
                List<NormalizedLandmark> image = result.handLandmarks[h].landmarks;
                List<Landmark> world = result.handWorldLandmarks[h].landmarks;
                if (image == null || world == null || image.Count < MediaPipeConversion.HandLandmarkCount || world.Count < MediaPipeConversion.HandLandmarkCount)
                {
                    continue;
                }

                for (int i = 0; i < MediaPipeConversion.HandLandmarkCount; i++)
                {
                    _world[i] = new Vector3(world[i].x, world[i].y, world[i].z);
                    _image[i] = new Vector2(image[i].x, image[i].y);
                }

                Category category = result.handedness != null && result.handedness.Count > h && result.handedness[h].categories?.Count > 0
                    ? result.handedness[h].categories[0]
                    : default;

                // Placed at zero; the main thread moves it to the body's wrist once its side is known.
                int slot = into.Count++;
                MediaPipeConversion.WriteHand(_world, _image, category.score, Vector3.zero, left: false, time, into.Hands[slot]);
                into.Handedness[slot] = category.categoryName;
            }
        }
    }

    /// <summary>Up to two faces as detected, handed to the main thread, which chooses the person's.</summary>
    internal sealed class FacesObservation
    {
        public const int Capacity = 2;

        public FacePose[] Faces { get; } = { new FacePose(), new FacePose() };

        /// <summary>Each face's nose tip in the image, to compare with the body's.</summary>
        public Vector2[] Noses { get; } = new Vector2[Capacity];

        public int Count { get; set; }

        public double Time { get; set; }

        /// <summary>
        /// Takes the face nearest the body's nose (tracking detailed design 6.3). A face elsewhere, such as
        /// a poster behind the person, is not used.
        /// </summary>
        public void AssignTo(TrackingFrame frame)
        {
            BodyPose body = frame.Body;
            int chosen = MediaPipeConversion.ChooseFace(Noses, Count, body.ImagePositions[(int)BodyJoint.Nose], body.IsUsable);
            if (chosen < 0)
            {
                frame.Face.Status = TrackingStatus.Lost;
                frame.Face.Timestamp = Time;
                return;
            }

            frame.Face.CopyFrom(Faces[chosen]);
        }
    }

    /// <summary>Up to two hands as detected, handed to the main thread, which decides their sides.</summary>
    internal sealed class HandsObservation
    {
        public HandPose[] Hands { get; } = { new HandPose(), new HandPose() };

        public string[] Handedness { get; } = new string[2];

        public int Count { get; set; }

        public double Time { get; set; }

        /// <summary>Decides each hand's side from the body's wrists and moves it there (tracking detailed design 6.4).</summary>
        public void AssignTo(TrackingFrame frame)
        {
            BodyPose body = frame.Body;
            bool bodySeen = body.IsUsable;
            Vector2 leftWrist = body.ImagePositions[(int)BodyJoint.LeftWrist];
            Vector2 rightWrist = body.ImagePositions[(int)BodyJoint.RightWrist];

            bool firstLeft = Count switch
            {
                1 => MediaPipeConversion.IsLeft(Hands[0].ImagePositions[0], bodySeen, leftWrist, rightWrist, Handedness[0]),
                2 => MediaPipeConversion.FirstIsLeft(Hands[0].ImagePositions[0], Hands[1].ImagePositions[0], bodySeen, leftWrist, rightWrist, Handedness[0]),
                _ => false,
            };

            HandPose first = Count >= 1 ? Hands[0] : null;
            HandPose second = Count == 2 ? Hands[1] : null;
            Place(frame.LeftHand, firstLeft ? first : second, left: true, body);
            Place(frame.RightHand, firstLeft ? second : first, left: false, body);
        }

        private void Place(HandPose target, HandPose detected, bool left, BodyPose body)
        {
            // Hands are not checked against the body's wrists: the two come from separate inferences,
            // often of different camera frames, and a moving hand is a frame's movement away from the
            // body's wrist. Dropping such hands made them flicker (tracked in step 2, 2026-10-19).
            if (detected == null)
            {
                target.Status = TrackingStatus.Lost;
                target.Timestamp = Time;
                return;
            }

            target.CopyFrom(detected);
            Vector3 wrist = body.IsUsable ? body[left ? BodyJoint.LeftWrist : BodyJoint.RightWrist] : Vector3.zero;
            for (int i = 0; i < HandPose.JointCount; i++)
            {
                target.Positions[i] += wrist;
            }

            target.PalmRotation = MediaPipeConversion.PalmRotation(
                target[HandJoint.Wrist], target[HandJoint.IndexMcp], target[HandJoint.MiddleMcp], target[HandJoint.LittleMcp], left);
        }
    }
}
