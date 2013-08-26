// Copyright (c) 2012, Event Store LLP
// All rights reserved.
// 
// Redistribution and use in source and binary forms, with or without
// modification, are permitted provided that the following conditions are
// met:
// 
// Redistributions of source code must retain the above copyright notice,
// this list of conditions and the following disclaimer.
// Redistributions in binary form must reproduce the above copyright
// notice, this list of conditions and the following disclaimer in the
// documentation and/or other materials provided with the distribution.
// Neither the name of the Event Store LLP nor the names of its
// contributors may be used to endorse or promote products derived from
// this software without specific prior written permission
// THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS
// "AS IS" AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT
// LIMITED TO, THE IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR
// A PARTICULAR PURPOSE ARE DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT
// HOLDER OR CONTRIBUTORS BE LIABLE FOR ANY DIRECT, INDIRECT, INCIDENTAL,
// SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES (INCLUDING, BUT NOT
// LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES; LOSS OF USE,
// DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND ON ANY
// THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT
// (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE
// OF THIS SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
// 

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using EventStore.Common.Options;
using EventStore.Common.Utils;
using EventStore.Core.Data;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace EventStore.Projections.Core.Services.Processing
{
    public class CheckpointTag : IComparable<CheckpointTag>
    {
        public readonly int Phase;
        public readonly TFPos Position;
        //TODO: rename to StreamsOrEventTypes or just Positions
        public readonly Dictionary<string, int> Streams;

        public readonly string CatalogStream;
        public readonly string DataStream;
        public readonly int CatalogPosition;
        public readonly int DataPosition;

        internal enum Mode
        {
            Phase,
            Position,
            Stream,
            MultiStream,
            EventTypeIndex,
            PreparePosition,
            ByStream
        }

        private CheckpointTag(int phase, bool completed)
        {
            Phase = phase;
            Position = completed ? new TFPos(long.MaxValue, long.MaxValue) : new TFPos(long.MinValue, long.MinValue);
            Streams = null;
            Mode_ = CalculateMode();
        }

        private CheckpointTag(int phase, TFPos position, Dictionary<string, int> streams)
        {
            Phase = phase;
            Position = position;
            Streams = streams;
            Mode_ = CalculateMode();
        }

        private CheckpointTag(int phase, long preparePosition)
        {
            Phase = phase;
            Position = new TFPos(long.MinValue, preparePosition);
            Mode_ = CalculateMode();
        }

        private CheckpointTag(int phase, TFPos position)
        {
            Phase = phase;
            Position = position;
            Mode_ = CalculateMode();
        }

        private CheckpointTag(int phase, IDictionary<string, int> streams)
        {
            Phase = phase;
            foreach (var stream in streams)
            {
                if (stream.Key == "") throw new ArgumentException("Empty stream name", "streams");
                if (stream.Value < 0 && stream.Value != ExpectedVersion.NoStream) throw new ArgumentException("Invalid sequence number", "streams");
            }
            Streams = new Dictionary<string, int>(streams); // clone
            Position = new TFPos(Int64.MinValue, Int64.MinValue);
            Mode_ = CalculateMode();
        }

        private CheckpointTag(int phase, IDictionary<string, int> eventTypes, TFPos position)
        {
            Phase = phase;
            Position = position;
            foreach (var stream in eventTypes)
            {
                if (stream.Key == "") throw new ArgumentException("Empty stream name", "eventTypes");
                if (stream.Value < 0 && stream.Value != ExpectedVersion.NoStream) throw new ArgumentException("Invalid sequence number", "eventTypes");
            }
            Streams = new Dictionary<string, int>(eventTypes); // clone
            Mode_ = CalculateMode();
        }

        private CheckpointTag(int phase, string stream, int sequenceNumber)
        {
            Phase = phase;
            if (stream == null) throw new ArgumentNullException("stream");
            if (stream == "") throw new ArgumentException("stream");
            if (sequenceNumber < 0 && sequenceNumber != ExpectedVersion.NoStream) throw new ArgumentException("sequenceNumber");
            Position = new TFPos(Int64.MinValue, Int64.MinValue);
            Streams = new Dictionary<string, int> {{stream, sequenceNumber}};
            Mode_ = CalculateMode();
        }

        private CheckpointTag(string catalogStream, int catalogPosition, string dataStream, int dataPosition)
        {
            CatalogStream = catalogStream;
            CatalogPosition = catalogPosition;
            DataStream = dataStream;
            DataPosition = dataPosition;
            Mode_ = Mode.ByStream;
        }

        private Mode CalculateMode()
        {
            if (Streams == null || Streams.Count == 0)
                if (Position.CommitPosition == Int64.MinValue && Position.PreparePosition == Int64.MinValue)
                    return Mode.Phase;
                else if (Position.CommitPosition == Int64.MaxValue && Position.PreparePosition == Int64.MaxValue)
                    return Mode.Phase;
                else if (Position.CommitPosition == Int64.MinValue && Position.PreparePosition != Int64.MinValue)
                    return Mode.PreparePosition;
                else
                    return Mode.Position;
            if (Position != new TFPos(Int64.MinValue, Int64.MinValue))
                return Mode.EventTypeIndex;
            if (Streams.Count == 1)
                return Mode.Stream;
            return Mode.MultiStream;
        }

        public static bool operator >(CheckpointTag left, CheckpointTag right)
        {
            if (ReferenceEquals(left, right))
                return false;
            if (!ReferenceEquals(left, null) && ReferenceEquals(right, null))
                return true;
            if (ReferenceEquals(left, null) && !ReferenceEquals(right, null))
                return false;
            if (left.Phase > right.Phase)
                return true;
            if (left.Phase < right.Phase)
                return false;
            var leftMode = left.Mode_;
            var rightMode = right.Mode_;
            UpgradeModes(ref leftMode, ref rightMode);
            if (leftMode != rightMode)
                throw new NotSupportedException("Cannot compare checkpoint tags in different modes");
            switch (leftMode)
            {
                case Mode.Phase:
                    return left.Position > right.Position;
                case Mode.Position:
                case Mode.EventTypeIndex:
                    return left.Position > right.Position;
                case Mode.PreparePosition:
                    return left.PreparePosition > right.PreparePosition;
                case Mode.Stream:
                    if (left.Streams.Keys.First() != right.Streams.Keys.First())
                        throw new InvalidOperationException("Cannot compare checkpoint tags across different streams");
                    var result = left.Streams.Values.First() > right.Streams.Values.First();
                    return result;
                case Mode.MultiStream:
                    int rvalue;
                    bool anyLeftGreater = left.Streams.Any(l => !right.Streams.TryGetValue(l.Key, out rvalue) || l.Value > rvalue);

                    int lvalue;
                    bool anyRightGreater = right.Streams.Any(r => !left.Streams.TryGetValue(r.Key, out lvalue) || r.Value > lvalue);

                    if (anyLeftGreater && anyRightGreater)
                        ThrowIncomparable(left, right);
                    return anyLeftGreater;
                default:
                    throw new NotSupportedException("Checkpoint tag mode is not supported in comparison");
            }
        }
        
        private static void ThrowIncomparable(CheckpointTag left, CheckpointTag right)
        {
            throw new InvalidOperationException(
                string.Format("Incomparable multi-stream checkpoint tags. '{0}' and '{1}'", left, right));
        }

        public static bool operator >=(CheckpointTag left, CheckpointTag right)
        {
            if (ReferenceEquals(left, right))
                return true;
            if (!ReferenceEquals(left, null) && ReferenceEquals(right, null))
                return true;
            if (ReferenceEquals(left, null) && !ReferenceEquals(right, null))
                return false;
            if (left.Phase > right.Phase)
                return true;
            if (left.Phase < right.Phase)
                return false;
            var leftMode = left.Mode_;
            var rightMode = right.Mode_;
            UpgradeModes(ref leftMode, ref rightMode);
            if (leftMode != rightMode)
                throw new NotSupportedException("Cannot compare checkpoint tags in different modes");
            switch (leftMode)
            {
                case Mode.Phase:
                    return left.Position >= right.Position;
                case Mode.Position:
                case Mode.EventTypeIndex:
                    return left.Position >= right.Position;
                case Mode.PreparePosition:
                    return left.PreparePosition >= right.PreparePosition;
                case Mode.Stream:
                    if (left.Streams.Keys.First() != right.Streams.Keys.First())
                        throw new InvalidOperationException("Cannot compare checkpoint tags across different streams");
                    var result = left.Streams.Values.First() >= right.Streams.Values.First();
                    return result;
                case Mode.MultiStream:
                    int rvalue;
                    bool anyLeftGreater = left.Streams.Any(l => !right.Streams.TryGetValue(l.Key, out rvalue) || l.Value > rvalue);

                    int lvalue;
                    bool anyRightGreater = right.Streams.Any(r => !left.Streams.TryGetValue(r.Key, out lvalue) || r.Value > lvalue);

                    if (anyLeftGreater && anyRightGreater)
                        ThrowIncomparable(left, right);
                    return !anyRightGreater;
                default:
                    throw new NotSupportedException("Checkpoint tag mode is not supported in comparison");
            }
        }

        public static bool operator <(CheckpointTag left, CheckpointTag right)
        {
            return !(left >= right);
        }

        public static bool operator <=(CheckpointTag left, CheckpointTag right)
        {
            return !(left > right);
        }

        public static bool operator ==(CheckpointTag left, CheckpointTag right)
        {
            return Equals(left, right);
        }

        public static bool operator !=(CheckpointTag left, CheckpointTag right)
        {
            return !(left == right);
        }

        protected bool Equals(CheckpointTag other)
        {
            if (Phase != other.Phase)
                return false;
            var leftMode = Mode_;
            var rightMode = other.Mode_;
            if (leftMode != rightMode)
                return false;
            UpgradeModes(ref leftMode, ref rightMode);
            switch (leftMode)
            {
                case Mode.Phase:
                    return Position == other.Position;
                case Mode.EventTypeIndex: 
                    // NOTE: we ignore stream positions as they are only suggestion on 
                    //       where to start to gain better performance
                    goto case Mode.Position;
                case Mode.Position:
                    return Position == other.Position;
                case Mode.PreparePosition:
                    return PreparePosition == other.PreparePosition;
                case Mode.Stream:
                    if (Streams.Keys.First() != other.Streams.Keys.First())
                        return false;
                    var result = Streams.Values.First() == other.Streams.Values.First();
                    return result;
                case Mode.MultiStream:
                    int rvalue;
                    return Streams.Count == other.Streams.Count
                           && Streams.All(l => other.Streams.TryGetValue(l.Key, out rvalue) && l.Value == rvalue);
                default:
                    throw new NotSupportedException("Checkpoint tag mode is not supported in comparison");
            }
        }

        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj)) return false;
            if (ReferenceEquals(this, obj)) return true;
            if (obj.GetType() != GetType()) return false;
            return Equals((CheckpointTag) obj);
        }

        public override int GetHashCode()
        {
            return Position.GetHashCode();
        }


        public long? CommitPosition
        {
            get
            {
                switch (Mode_)
                {
                    case Mode.Position:
                    case Mode.EventTypeIndex:
                        return Position.CommitPosition;
                    default:
                        return null;
                }
            }
        }

        public long? PreparePosition
        {
            get
            {
                switch (Mode_)
                {
                    case Mode.Position:
                    case Mode.PreparePosition:
                    case Mode.EventTypeIndex:
                        return Position.PreparePosition;
                    default:
                        return null;
                }
            }
        }

        public static CheckpointTag Empty {
            get { return _empty; }
        }

        internal readonly Mode Mode_;
        private static readonly CheckpointTag _empty = new CheckpointTag(-1, false);

        public static CheckpointTag FromPhase(int phase, bool completed)
        {
            return new CheckpointTag(phase, completed);
        }

        public static CheckpointTag FromPosition(int phase, long commitPosition, long preparePosition)
        {
            return new CheckpointTag(phase, new TFPos(commitPosition, preparePosition));
        }

        public static CheckpointTag FromPosition(int phase, TFPos position)
        {
            return new CheckpointTag(phase, position);
        }

        public static CheckpointTag FromPreparePosition(int phase, long preparePosition)
        {
            return new CheckpointTag(phase, preparePosition);
        }

        public static CheckpointTag FromStreamPosition(int phase, string stream, int sequenceNumber)
        {
            return new CheckpointTag(phase, stream, sequenceNumber);
        }

        public static CheckpointTag FromStreamPositions(int phase, IDictionary<string, int> streams)
        {
            // streams cloned inside
            return new CheckpointTag(phase, streams);
        }

        public static CheckpointTag FromEventTypeIndexPositions(int phase, TFPos position, IDictionary<string, int> streams)
        {
            // streams cloned inside
            return new CheckpointTag(phase, streams, position);
        }

        public int CompareTo(CheckpointTag other)
        {
            return this < other ? -1 : (this > other ? 1 : 0);
        }

        public override string ToString()
        {
            string result;
            switch (Mode_)
            {
                case Mode.Phase:
                    return "Phase: " + Phase + (Completed ? " (completed)" : "");
                case Mode.Position:
                    result = Position.ToString();
                    break;
                case Mode.PreparePosition:
                    result = PreparePosition.ToString();
                    break;
                case Mode.Stream:
                    result = Streams.Keys.First() + ": " + Streams.Values.First();
                    break;
                case Mode.MultiStream:
                case Mode.EventTypeIndex:
                    var sb = new StringBuilder();
                    if (Mode_ == Mode.EventTypeIndex)
                    {
                        sb.Append(Position.ToString());
                        sb.Append("; ");
                    }
                    foreach (var stream in Streams)
                    {
                        sb.AppendFormat("{0}: {1}; ", stream.Key, stream.Value);
                    }
                    result = sb.ToString();
                    break;
                default:
                    return "Unsupported mode: " + base.ToString();
            }
            if (Phase == 0)
                return result;
            else
            {
                return "(" + Phase + ") " + result;
            }
        }

        public bool Completed
        {
            get { return Position.CommitPosition == Int64.MaxValue; }
        }

        private static void UpgradeModes(ref Mode leftMode, ref Mode rightMode)
        {
            if (leftMode == Mode.Stream && rightMode == Mode.MultiStream)
            {
                leftMode = Mode.MultiStream;
                return;
            }
            if (leftMode == Mode.MultiStream && rightMode == Mode.Stream)
            {
                rightMode = Mode.MultiStream;
                return;
            }
            if (leftMode == Mode.Position && rightMode == Mode.EventTypeIndex)
            {
                leftMode = Mode.EventTypeIndex;
                return;
            }
            if (leftMode == Mode.EventTypeIndex && rightMode == Mode.Position)
            {
                rightMode = Mode.EventTypeIndex;
                return;
            }
        }

        public CheckpointTag UpdateStreamPosition(string streamId, int eventSequenceNumber)
        {
            if (Mode_ != Mode.MultiStream)
                throw new ArgumentException("Invalid tag mode", "tag");
            var resultDictionary = PatchStreamsDictionary(streamId, eventSequenceNumber);
            return FromStreamPositions(Phase, resultDictionary);
        }

        public CheckpointTag UpdateEventTypeIndexPosition(TFPos position, string eventType, int eventSequenceNumber)
        {
            if (Mode_ != Mode.EventTypeIndex)
                throw new ArgumentException("Invalid tag mode", "tag");
            var resultDictionary = PatchStreamsDictionary(eventType, eventSequenceNumber);
            return FromEventTypeIndexPositions(Phase, position, resultDictionary);
        }

        public CheckpointTag UpdateEventTypeIndexPosition(TFPos position)
        {
            if (Mode_ != Mode.EventTypeIndex)
                throw new ArgumentException("Invalid tag mode", "tag");
            return FromEventTypeIndexPositions(Phase, position, Streams);
        }

        private Dictionary<string, int> PatchStreamsDictionary(string streamId, int eventSequenceNumber)
        {
            var resultDictionary = new Dictionary<string, int>();
            var was = false;
            foreach (var stream in Streams)
            {
                if (stream.Key == streamId)
                {
                    was = true;
                    if (eventSequenceNumber < stream.Value)
                        resultDictionary.Add(stream.Key, stream.Value);
                    else 
                        resultDictionary.Add(stream.Key, eventSequenceNumber);
                }
                else
                {
                    resultDictionary.Add(stream.Key, stream.Value);
                }
            }
            if (!was)
                throw new ArgumentException("Key not found: " + streamId, "streamId");
            if (resultDictionary.Count < Streams.Count)
                resultDictionary.Add(streamId, eventSequenceNumber);
            return resultDictionary;
        }

        public byte[] ToJsonBytes(ProjectionVersion projectionVersion, IEnumerable<KeyValuePair<string, JToken>> extraMetaData = null)
        {
            if (projectionVersion.ProjectionId == -1) throw new ArgumentException("projectionId is required", "projectionVersion");

            using (var memoryStream = new MemoryStream())
            {
                using (var textWriter = new StreamWriter(memoryStream, Helper.UTF8NoBom))
                using (var jsonWriter = new JsonTextWriter(textWriter))
                {
                    WriteTo(projectionVersion, extraMetaData, jsonWriter);
                }
                return memoryStream.ToArray();
            }
        }

        public string ToJsonString(ProjectionVersion projectionVersion, IEnumerable<KeyValuePair<string, JToken>> extraMetaData = null)
        {
            if (projectionVersion.ProjectionId == -1) throw new ArgumentException("projectionId is required", "projectionVersion");

            using (var textWriter = new StringWriter())
            {
                using (var jsonWriter = new JsonTextWriter(textWriter))
                {
                    WriteTo(projectionVersion, extraMetaData, jsonWriter);
                }
                return textWriter.ToString();
            }
        }

        public string ToJsonString(IEnumerable<KeyValuePair<string, JToken>> extraMetaData = null)
        {
            using (var textWriter = new StringWriter())
            {
                using (var jsonWriter = new JsonTextWriter(textWriter))
                {
                    WriteTo(default(ProjectionVersion), extraMetaData, jsonWriter);
                }
                return textWriter.ToString();
            }
        }

        public JRaw ToJsonRaw(IEnumerable<KeyValuePair<string, JToken>> extraMetaData = null)
        {
            using (var textWriter = new StringWriter())
            {
                using (var jsonWriter = new JsonTextWriter(textWriter))
                {
                    WriteTo(default(ProjectionVersion), extraMetaData, jsonWriter);
                }
                return new JRaw(textWriter.ToString());
            }
        }

        private void WriteTo(ProjectionVersion projectionVersion, IEnumerable<KeyValuePair<string, JToken>> extraMetaData, JsonTextWriter jsonWriter)
        {
            jsonWriter.WriteStartObject();
            if (projectionVersion.ProjectionId > 0)
            {
                jsonWriter.WritePropertyName("$v");
                WriteVersion(projectionVersion, jsonWriter);
            }
            if (Phase != 0)
            {
                jsonWriter.WritePropertyName("$ph");
                jsonWriter.WriteValue(Phase);
            }
            switch (Mode_)
            {
                case Mode.Phase:
                    jsonWriter.WritePropertyName("$cp");
                    jsonWriter.WriteValue(Completed);
                    break;
                case Mode.Position:
                case Mode.EventTypeIndex:
                    jsonWriter.WritePropertyName("$c");
                    jsonWriter.WriteValue(CommitPosition.GetValueOrDefault());
                    jsonWriter.WritePropertyName("$p");
                    jsonWriter.WriteValue(PreparePosition.GetValueOrDefault());
                    if (Mode_ == Mode.EventTypeIndex)
                        goto case Mode.MultiStream;
                    break;
                case Mode.PreparePosition:
                    jsonWriter.WritePropertyName("$p");
                    jsonWriter.WriteValue(PreparePosition.GetValueOrDefault());
                    break;
                case Mode.Stream:
                case Mode.MultiStream:
                    jsonWriter.WritePropertyName("$s");
                    jsonWriter.WriteStartObject();
                    foreach (var stream in Streams)
                    {
                        jsonWriter.WritePropertyName(stream.Key);
                        jsonWriter.WriteValue(stream.Value);
                    }
                    jsonWriter.WriteEndObject();
                    break;
            }
            if (extraMetaData != null)
            {
                foreach (var pair in extraMetaData)
                {
                    jsonWriter.WritePropertyName(pair.Key);
                    pair.Value.WriteTo(jsonWriter);
                }
            }
            jsonWriter.WriteEndObject();
        }

        private static void WriteVersion(ProjectionVersion projectionVersion, JsonTextWriter jsonWriter)
        {
            jsonWriter.WriteValue(
                projectionVersion.ProjectionId + ":" + projectionVersion.Epoch + ":" + projectionVersion.Version + ":"
                + Projections.VERSION);
        }

        public static CheckpointTagVersion FromJson(JsonReader reader, ProjectionVersion current)
        {
            Check(reader.Read(), reader);
            Check(JsonToken.StartObject, reader);
            long? commitPosition = null;
            long? preparePosition = null;
            Dictionary<string, int> streams = null;
            Dictionary<string, JToken> extra = null;
            var projectionId = current.ProjectionId;
            var projectionEpoch = 0;
            var projectionVersion = 0;
            var projectionSystemVersion = 0;
            var projectionPhase = 0;
            while (true)
            {
                Check(reader.Read(), reader);
                if (reader.TokenType == JsonToken.EndObject)
                    break;
                Check(JsonToken.PropertyName, reader);
                var name = (string) reader.Value;
                switch (name)
                {
                    case "$cp":
                        Check(reader.Read(), reader);
                        var completed = (bool)reader.Value;
                        commitPosition = completed ? Int64.MaxValue : Int64.MinValue;
                        preparePosition = completed ? Int64.MaxValue : Int64.MinValue;
                        break;
                    case "$v":
                    case "v":
                        Check(reader.Read(), reader);
                        if (reader.ValueType == typeof (long))
                        {
                            var v = (int)(long)reader.Value;
                            if (v > 0) // TODO: remove this if with time
                                projectionVersion = v;
                        }
                        else
                        {
                            //TODO: better handle errors
                            var v = (string) reader.Value;
                            string[] parts = v.Split(':');
                            if (parts.Length == 2)
                            {
                                projectionVersion = Int32.Parse(parts[1]);
                            }
                            else
                            {
                                projectionId = Int32.Parse(parts[0]);
                                projectionEpoch = Int32.Parse(parts[1]);
                                projectionVersion = Int32.Parse(parts[2]);
                                if (parts.Length >= 4) 
                                    projectionSystemVersion = Int32.Parse(parts[3]);
                            }
                        }
                        break;
                    case "$c":
                    case "c":
                    case "commitPosition":
                        Check(reader.Read(), reader);
                        commitPosition = (long) reader.Value;
                        break;
                    case "$p":
                    case "p":
                    case "preparePosition":
                        Check(reader.Read(), reader);
                        preparePosition = (long) reader.Value;
                        break;
                    case "$s":
                    case "s":
                    case "streams":
                        Check(reader.Read(), reader);
                        Check(JsonToken.StartObject, reader);
                        streams = new Dictionary<string, int>();
                        while (true)
                        {
                            Check(reader.Read(), reader);
                            if (reader.TokenType == JsonToken.EndObject)
                                break;
                            Check(JsonToken.PropertyName, reader);
                            var streamName = (string) reader.Value;
                            Check(reader.Read(), reader);
                            var position = (int)(long) reader.Value;
                            streams.Add(streamName, position);
                        }
                        break;
                    case "$ph":
                        Check(reader.Read(), reader);
                        projectionPhase = (int)(long) reader.Value;
                        break;
                    default:
                        if (extra == null)
                            extra = new Dictionary<string, JToken>();
                        Check(reader.Read(), reader);
                        var jToken = JToken.ReadFrom(reader);
                        extra.Add(name, jToken);
                        break;
                }
            }
            return new CheckpointTagVersion
            {
                Tag =
                    new CheckpointTag(
                        projectionPhase, new TFPos(commitPosition ?? Int64.MinValue, preparePosition ?? Int64.MinValue),
                        streams),
                Version = new ProjectionVersion(projectionId, projectionEpoch, projectionVersion),
                SystemVersion = projectionSystemVersion,
                ExtraMetadata = extra,
            };
        }

        public static void Check(JsonToken type, JsonReader reader)
        {
            if (reader.TokenType != type)
                throw new Exception("Invalid JSON");
        }

        public static void Check(bool read, JsonReader reader)
        {
            if (!read)
                throw new Exception("Invalid JSON");
        }
    }
}
