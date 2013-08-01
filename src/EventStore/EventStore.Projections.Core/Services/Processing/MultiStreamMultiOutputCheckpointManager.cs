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
using System.Security.Principal;
using EventStore.Common.Utils;
using EventStore.Core.Bus;
using EventStore.Core.Data;
using EventStore.Core.Helpers;
using EventStore.Core.Services.UserManagement;
using EventStore.Projections.Core.Messages;

namespace EventStore.Projections.Core.Services.Processing
{
    public class MultiStreamMultiOutputCheckpointManager : DefaultCheckpointManager, IEmittedStreamContainer
    {
        private readonly PositionTagger _positionTagger;
        private CheckpointTag _lastOrderCheckpointTag; //TODO: use position tracker to ensure order?
        private EmittedStream _orderStream;
        private bool _orderStreamReadingCompleted;
        private int _loadingItemsCount;
        private readonly Stack<Item> _loadQueue = new Stack<Item>();
        private CheckpointTag _loadingPrerecordedEventsFrom;

        public MultiStreamMultiOutputCheckpointManager(
            IPublisher publisher, Guid projectionCorrelationId, ProjectionVersion projectionVersion, IPrincipal runAs,
            IODispatcher ioDispatcher, ProjectionConfig projectionConfig, string name, PositionTagger positionTagger,
            ProjectionNamesBuilder namingBuilder, IResultEmitter resultEmitter, bool useCheckpoints,
            bool emitPartitionCheckpoints = false)
            : base(
                publisher, projectionCorrelationId, projectionVersion, runAs, ioDispatcher, projectionConfig, name,
                positionTagger, namingBuilder, resultEmitter, useCheckpoints, emitPartitionCheckpoints)
        {
            _positionTagger = positionTagger;
        }

        public override void Initialize()
        {
            base.Initialize();
            _lastOrderCheckpointTag = null;
            if (_orderStream != null) _orderStream.Dispose();
            _orderStream = null;
        }

        public override void Start(CheckpointTag checkpointTag)
        {
            base.Start(checkpointTag);
            _orderStream = CreateOrderStream(checkpointTag);
            _orderStream.Start();
        }

        public override void RecordEventOrder(
            ResolvedEvent resolvedEvent, CheckpointTag orderCheckpointTag, Action committed)
        {
            EnsureStarted();
            if (_stopping)
                throw new InvalidOperationException("Stopping");
            var orderStreamName = _namingBuilder.GetOrderStreamName();
            //TODO: -order stream requires correctly configured event expiration. 
            // the best is to truncate using $startFrom, but $maxAge should be also acceptable
            _orderStream.EmitEvents(
                new[]
                {
                    new EmittedDataEvent(
                        orderStreamName, null, Guid.NewGuid(), "$>",
                        resolvedEvent.PositionSequenceNumber + "@" + resolvedEvent.PositionStreamId, null,
                        orderCheckpointTag, _lastOrderCheckpointTag, v => committed())
                });
            _lastOrderCheckpointTag = orderCheckpointTag;
        }

        private EmittedStream CreateOrderStream(CheckpointTag from)
        {
            //TODO: this stream requires $startFrom to be updated from time to time to reduce space taken
            return new EmittedStream(
                /* MUST NEVER SEND READY MESSAGE */
                _namingBuilder.GetOrderStreamName(),
                new EmittedStream.WriterConfiguration(new EmittedStream.WriterConfiguration.StreamMetadata(), SystemAccount.Principal, 100, _logger), _projectionVersion,
                _positionTagger, @from, _ioDispatcher, this, noCheckpoints: true);
        }

        public override void GetStatistics(ProjectionStatistics info)
        {
            base.GetStatistics(info);
            if (_orderStream != null)
            {
                info.WritePendingEventsAfterCheckpoint += _orderStream.GetWritePendingEvents();
                info.ReadsInProgress += _orderStream.GetReadsInProgress();
                info.WritesInProgress += _orderStream.GetWritesInProgress();
            }
        }


        protected override void BeginLoadPrerecordedEvents(CheckpointTag checkpointTag)
        {
            BeginLoadPrerecordedEventsChunk(checkpointTag, -1);
        }

        private void BeginLoadPrerecordedEventsChunk(CheckpointTag checkpointTag, int fromEventNumber)
        {
            _loadingPrerecordedEventsFrom = checkpointTag;
            _ioDispatcher.ReadBackward(
                _namingBuilder.GetOrderStreamName(), fromEventNumber, 100, false, SystemAccount.Principal, completed =>
                {
                    switch (completed.Result)
                    {
                        case ReadStreamResult.NoStream:
                            _lastOrderCheckpointTag = _positionTagger.MakeZeroCheckpointTag();
                            PrerecordedEventsLoaded(checkpointTag);
                            break;
                        case ReadStreamResult.Success:
                            var epochEnded = false;
                            foreach (var @event in completed.Events)
                            {
                                var parsed = @event.Event.Metadata.ParseCheckpointTagVersionExtraJson(
                                    _projectionVersion);
                                //TODO: throw exception if different projectionID?
                                if (_projectionVersion.ProjectionId != parsed.Version.ProjectionId
                                    || _projectionVersion.Epoch > parsed.Version.Version)
                                {
                                    epochEnded = true;
                                    break;
                                }
                                var tag = parsed.AdjustBy(_positionTagger, _projectionVersion);
                                //NOTE: even if this tag <= checkpointTag we set last tag
                                // this is to know the exact last tag to request when writing
                                if (_lastOrderCheckpointTag == null)
                                    _lastOrderCheckpointTag = tag;

                                if (tag <= checkpointTag)
                                {
                                    SetOrderStreamReadCompleted();
                                    break;
                                }
                                EnqueuePrerecordedEvent(@event.Event, tag);
                            }
                            if (epochEnded || completed.IsEndOfStream)
                                SetOrderStreamReadCompleted();
                            else
                                BeginLoadPrerecordedEventsChunk(checkpointTag, completed.NextEventNumber);
                            break;
                        default:
                            throw new Exception("Cannot read order stream");
                    }
                });
        }

        private void EnqueuePrerecordedEvent(EventRecord @event, CheckpointTag tag)
        {
            if (@event == null) throw new ArgumentNullException("event");
            if (tag == null) throw new ArgumentNullException("tag");
            if (@event.EventType != "$>")
                throw new ArgumentException("linkto ($>) event expected", "event");

            _loadingItemsCount++;

            var item = new Item(tag);
            _loadQueue.Push(item);
            //NOTE: we do manual link-to resolution as we write links to the position events
            //      which may in turn be a link.  This is necessary to provide a correct 
            //       ResolvedEvent when replaying from the -order stream
            var linkTo = Helper.UTF8NoBom.GetString(@event.Data);
            string[] parts = linkTo.Split('@');
            int eventNumber = int.Parse(parts[0]);
            string streamId = parts[1];


            _ioDispatcher.ReadBackward(
                streamId, eventNumber, 1, true, SystemAccount.Principal, completed =>
                {
                    switch (completed.Result)
                    {
                        case ReadStreamResult.Success:
                            if (completed.Events.Length != 1)
                                throw new Exception(
                                    string.Format("Cannot read {0}. Error: {1}", linkTo, completed.Error));
                            item.SetLoadedEvent(completed.Events[0]);
                            _loadingItemsCount--;
                            CheckAllEventsLoaded();
                            break;
                        default:
                            throw new Exception(string.Format("Cannot read {0}. Error: {1}", linkTo, completed.Error));
                    }
                });
        }

        private void CheckAllEventsLoaded()
        {
            CheckpointTag lastTag = null;
            if (_orderStreamReadingCompleted && _loadingItemsCount == 0)
            {
                var number = 0;
                while (_loadQueue.Count > 0)
                {
                    var item = _loadQueue.Pop();
                    var @event = item._result;
                    lastTag = item.Tag;
                    SendPrerecordedEvent(@event, lastTag, number);
                    number++;
                }

                _loadingItemsCount = -1; // completed - do not dispatch one more time
                PrerecordedEventsLoaded(lastTag ?? _loadingPrerecordedEventsFrom);
            }
        }

        private void SetOrderStreamReadCompleted()
        {
            _orderStreamReadingCompleted = true;
            CheckAllEventsLoaded();
        }

        private class Item
        {
            internal EventStore.Core.Data.ResolvedEvent _result;
            private readonly CheckpointTag _tag;

            public Item(CheckpointTag tag)
            {
                _tag = tag;
            }

            public CheckpointTag Tag
            {
                get { return _tag; }
            }

            public void SetLoadedEvent(EventStore.Core.Data.ResolvedEvent eventLinkPair)
            {
                _result = eventLinkPair;
            }
        }

        public void Handle(CoreProjectionProcessingMessage.EmittedStreamAwaiting message)
        {
            if (_stopped)
                return;
            throw new NotImplementedException();
        }

        public void Handle(CoreProjectionProcessingMessage.EmittedStreamWriteCompleted message)
        {
            if (_stopped)
                return;
        }
    }
}
