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


using System;
using System.Linq;
using EventStore.Core.Authentication;
using EventStore.Core.Messages;
using EventStore.Core.Services.Transport.Http.Authentication;
using EventStore.Projections.Core.Services.Processing;
using NUnit.Framework;

namespace EventStore.Projections.Core.Tests.Services.core_projection.emitted_stream
{
    [TestFixture]
    public class when_handling_an_emit_with_write_as_configured : TestFixtureWithExistingEvents
    {
        private EmittedStream _stream;
        private TestCheckpointManagerMessageHandler _readyHandler;
        private OpenGenericPrincipal _writeAs;

        protected override void Given()
        {
            AllWritesQueueUp();
            ExistingEvent("test_stream", "type", @"{""c"": 100, ""p"": 50}", "data");
        }

        [SetUp]
        public void setup()
        {
            _readyHandler = new TestCheckpointManagerMessageHandler();
            _writeAs = new OpenGenericPrincipal("test-user");
            _stream = new EmittedStream(
                "test_stream", new EmittedStream.WriterConfiguration(new EmittedStream.WriterConfiguration.StreamMetadata(), _writeAs, maxWriteBatchLength: 50),
                new ProjectionVersion(1, 0, 0), new TransactionFilePositionTagger(0), CheckpointTag.FromPosition(0, 40, 30),
                _ioDispatcher, _readyHandler);
            _stream.Start();

            _stream.EmitEvents(
                new[]
                {
                    new EmittedDataEvent(
                        "test_stream", Guid.NewGuid(), "type", true, "data", null, CheckpointTag.FromPosition(0, 200, 150), null)
                });
        }

        [Test]
        public void publishes_not_yet_published_events()
        {
            Assert.AreEqual(1, _consumer.HandledMessages.OfType<ClientMessage.WriteEvents>().Count());
        }

        [Test]
        public void publishes_write_event_with_correct_user_account()
        {
            var writeEvent = _consumer.HandledMessages.OfType<ClientMessage.WriteEvents>().Single();

            Assert.AreSame(_writeAs, writeEvent.User);
        }
    }
}
