using System;
using System.Collections.Generic;

namespace VirtualVessel.Diagnostics.Logging.Pipeline
{
    /// <summary>
    /// <see cref="ILog"/> bound to a module, category, and immutable context.
    /// </summary>
    internal sealed class PipelineLog : ILog
    {
        private readonly LogPipeline _pipeline;
        private readonly string _module;
        private readonly string _category;
        private readonly KeyValuePair<string, string>[] _context;

        public PipelineLog(LogPipeline pipeline, string module, string category, KeyValuePair<string, string>[] context)
        {
            _pipeline = pipeline ?? throw new ArgumentNullException(nameof(pipeline));
            _module = module ?? throw new ArgumentNullException(nameof(module));
            _category = category;
            _context = context;
        }

        public bool IsEnabled(LogLevel level)
        {
            return _pipeline.IsEnabled(level, _module);
        }

        public void Write(LogLevel level, string message, Exception exception = null)
        {
            _pipeline.Write(level, _module, _category, message, null, _context, exception);
        }

        public void Write(LogLevel level, string message, IReadOnlyList<LogProperty> properties, Exception exception = null)
        {
            _pipeline.Write(level, _module, _category, message, properties, _context, exception);
        }

        public ILog WithContext(string key, string value)
        {
            if (string.IsNullOrEmpty(key))
            {
                throw new ArgumentException("Context key must not be empty.", nameof(key));
            }

            int length = _context?.Length ?? 0;
            var context = new KeyValuePair<string, string>[length + 1];
            if (length > 0)
            {
                Array.Copy(_context, context, length);
            }

            context[length] = new KeyValuePair<string, string>(key, value);
            return new PipelineLog(_pipeline, _module, _category, context);
        }
    }

    internal sealed class PipelineLogProvider : ILogProvider
    {
        private readonly LogPipeline _pipeline;

        public PipelineLogProvider(LogPipeline pipeline)
        {
            _pipeline = pipeline ?? throw new ArgumentNullException(nameof(pipeline));
        }

        public ILog GetLog(string module, string category = null)
        {
            if (string.IsNullOrWhiteSpace(module))
            {
                throw new ArgumentException("Module name must not be empty.", nameof(module));
            }

            return new PipelineLog(_pipeline, module, category, null);
        }
    }
}
