using System;

namespace NLog.Loki.Model
{
    public class LokiEvent
    {
        public LokiLabels Labels { get; }

        public DateTime Timestamp { get; }

        public string Line { get; }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="labels"></param>
        /// <param name="timestamp">.ToUniversalTime() will be called on this date before it will be used</param>
        /// <param name="line"></param>
        /// <exception cref="ArgumentNullException"></exception>
        public LokiEvent(LokiLabels labels, DateTime timestamp, string line)
        {
            Labels = labels ?? throw new ArgumentNullException(nameof(labels));
            Timestamp = timestamp;
            Line = line ?? throw new ArgumentNullException(nameof(line));
        }
    }
}
