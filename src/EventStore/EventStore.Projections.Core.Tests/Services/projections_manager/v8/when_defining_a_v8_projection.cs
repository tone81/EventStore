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

using NUnit.Framework;

namespace EventStore.Projections.Core.Tests.Services.projections_manager.v8
{
    public static class when_defining_a_v8_projection
    {
        [TestFixture]
        public class with_from_all_source : TestFixtureWithJsProjection
        {
            protected override void Given()
            {
                _projection = @"
                    fromAll().whenAny(
                        function(state, event) {
                            return state;
                        });
                ";
                _state = @"{""count"": 0}";
            }

            [Test, Category("v8")]
            public void source_definition_is_correct()
            {
                Assert.AreEqual(true, _source.AllStreams);
                Assert.That(_source.Streams == null || _source.Streams.Length == 0);
                Assert.That(_source.Categories == null || _source.Categories.Length== 0);
                Assert.AreEqual(false, _source.ByStreams);
            }
        }

        [TestFixture]
        public class with_from_stream : TestFixtureWithJsProjection
        {
            protected override void Given()
            {
                _projection = @"
                    fromStream('stream1').whenAny(
                        function(state, event) {
                            return state;
                        });
                ";
                _state = @"{""count"": 0}";
            }

            [Test, Category("v8")]
            public void source_definition_is_correct()
            {
                Assert.AreEqual(false, _source.AllStreams);
                Assert.IsNotNull(_source.Streams);
                Assert.AreEqual(1, _source.Streams.Length);
                Assert.AreEqual("stream1", _source.Streams[0]);
                Assert.That(_source.Categories == null || _source.Categories.Length == 0);
                Assert.AreEqual(false, _source.ByStreams);
            }
        }

        [TestFixture]
        public class with_multiple_from_streams : TestFixtureWithJsProjection
        {
            protected override void Given()
            {
                _projection = @"
                    fromStreams(['stream1', 'stream2', 'stream3']).whenAny(
                        function(state, event) {
                            return state;
                        });
                ";
                _state = @"{""count"": 0}";
            }

            [Test, Category("v8")]
            public void source_definition_is_correct()
            {
                Assert.AreEqual(false, _source.AllStreams);
                Assert.IsNotNull(_source.Streams);
                Assert.AreEqual(3, _source.Streams.Length);
                Assert.AreEqual("stream1", _source.Streams[0]);
                Assert.AreEqual("stream2", _source.Streams[1]);
                Assert.AreEqual("stream3", _source.Streams[2]);
                Assert.That(_source.Categories == null || _source.Categories.Length == 0);
                Assert.AreEqual(false, _source.ByStreams);
            }
        }

        [TestFixture]
        public class with_multiple_from_streams_plain : TestFixtureWithJsProjection
        {
            protected override void Given()
            {
                _projection = @"
                    fromStreams('stream1', 'stream2', 'stream3').whenAny(
                        function(state, event) {
                            return state;
                        });
                ";
                _state = @"{""count"": 0}";
            }

            [Test, Category("v8")]
            public void source_definition_is_correct()
            {
                Assert.AreEqual(false, _source.AllStreams);
                Assert.IsNotNull(_source.Streams);
                Assert.AreEqual(3, _source.Streams.Length);
                Assert.AreEqual("stream1", _source.Streams[0]);
                Assert.AreEqual("stream2", _source.Streams[1]);
                Assert.AreEqual("stream3", _source.Streams[2]);
                Assert.That(_source.Categories == null || _source.Categories.Length == 0);
                Assert.AreEqual(false, _source.ByStreams);
            }
        }

        [TestFixture]
        public class with_from_category : TestFixtureWithJsProjection
        {
            protected override void Given()
            {
                _projection = @"
                    fromCategory('category1').whenAny(
                        function(state, event) {
                            return state;
                        });
                ";
                _state = @"{""count"": 0}";
            }

            [Test, Category("v8")]
            public void source_definition_is_correct()
            {
                Assert.AreEqual(false, _source.AllStreams);
                Assert.IsNotNull(_source.Categories);
                Assert.AreEqual(1, _source.Categories.Length);
                Assert.AreEqual("category1", _source.Categories[0]);
                Assert.That(_source.Streams == null || _source.Streams.Length == 0);
                Assert.AreEqual(false, _source.ByStreams);
            }
        }

        [TestFixture]
        public class with_from_category_by_stream : TestFixtureWithJsProjection
        {
            protected override void Given()
            {
                _projection = @"
                    fromCategory('category1').foreachStream().whenAny(
                        function(state, event) {
                            return state;
                        });
                ";
                _state = @"{""count"": 0}";
            }

            [Test, Category("v8")]
            public void source_definition_is_correct()
            {
                Assert.AreEqual(false, _source.AllStreams);
                Assert.IsNotNull(_source.Categories);
                Assert.AreEqual(1, _source.Categories.Length);
                Assert.AreEqual("category1", _source.Categories[0]);
                Assert.That(_source.Streams == null || _source.Streams.Length == 0);
                Assert.AreEqual(true, _source.ByStreams);
            }
        }

        [TestFixture]
        public class with_from_all_by_custom_partitions : TestFixtureWithJsProjection
        {
            protected override void Given()
            {
                _projection = @"
                    fromAll().partitionBy(function(event){
                        return event.eventType;
                    }).whenAny(
                        function(state, event) {
                            return state;
                        });
                ";
                _state = @"{""count"": 0}";
            }

            [Test]
            public void source_definition_is_correct()
            {
                Assert.AreEqual(true, _source.AllStreams);
                Assert.That(_source.Categories == null || _source.Categories.Length == 0);
                Assert.That(_source.Streams == null || _source.Streams.Length == 0);
                Assert.AreEqual(true, _source.ByCustomPartitions);
                Assert.AreEqual(false, _source.ByStreams);
            }
        }

        [TestFixture]
        public class with_output_to : TestFixtureWithJsProjection
        {
            protected override void Given()
            {
                _projection = @"
                    fromAll()
                    .whenAny(
                        function(state, event) {
                            return state;
                        })
                    .$defines_state_transform();
                ";
                _state = @"{""count"": 0}";
            }

            [Test]
            public void source_definition_is_correct()
            {
                Assert.AreEqual(true, _source.DefinesStateTransform);
            }
        }

        [TestFixture]
        public class with_transform_by : TestFixtureWithJsProjection
        {
            protected override void Given()
            {
                _projection = @"
                    fromAll().whenAny(
                        function(state, event) {
                            return state;
                        }).transformBy(function(s) {return s;});
                ";
                _state = @"{""count"": 0}";
            }
            [Test, Category("v8")]
            public void source_definition_is_correct()
            {
                Assert.AreEqual(true, _source.DefinesStateTransform);
            }
        }

        public class with_filter_by : TestFixtureWithJsProjection
        {
            protected override void Given()
            {
                _projection = @"
                    fromAll().when({
                        some: function(state, event) {
                            return state;
                        }
                    }).filterBy(function(s) {return true;});
                ";
                _state = @"{""count"": 0}";
            }

            [Test, Category("v8")]
            public void source_definition_is_correct()
            {
                Assert.AreEqual(true, _source.DefinesStateTransform);
            }
        }

        [TestFixture]
        public class with_state_stream_name_option : TestFixtureWithJsProjection
        {
            protected override void Given()
            {
                _projection = @"
                    options({
                        resultStreamName: 'state-stream',
                    });
                    fromAll().whenAny(
                        function(state, event) {
                            return state;
                        });
                ";
                _state = @"{""count"": 0}";
            }

            [Test, Category("v8")]
            public void source_definition_is_correct()
            {
                Assert.AreEqual("state-stream", _source.ResultStreamNameOption);
            }
        }

        [TestFixture]
        public class with_include_links_option : TestFixtureWithJsProjection
        {
            protected override void Given()
            {
                _projection = @"
                    options({
                        $includeLinks: true,
                    });
                    fromAll().whenAny(
                        function(state, event) {
                            return state;
                        });
                ";
                _state = @"{""count"": 0}";
            }

            [Test, Category("v8")]
            public void source_definition_is_correct()
            {
                Assert.AreEqual(true, _source.IncludeLinksOption);
            }
        }

        [TestFixture]
        public class with_reorder_events_option : TestFixtureWithJsProjection
        {
            protected override void Given()
            {
                _projection = @"
                    options({
                        reorderEvents: true,
                        processingLag: 500,
                    });
                    fromAll().whenAny(
                        function(state, event) {
                            return state;
                        });
                ";
                _state = @"{""count"": 0}";
            }

            [Test, Category("v8")]
            public void source_definition_is_correct()
            {
                Assert.AreEqual(500, _source.ProcessingLagOption);
                Assert.AreEqual(true, _source.ReorderEventsOption);
            }
        }

        [TestFixture]
        public class with_multiple_option_statements : TestFixtureWithJsProjection
        {
            protected override void Given()
            {
                _projection = @"
                    options({
                        reorderEvents: false,
                        processingLag: 500,
                    });
                    options({
                        reorderEvents: true,
                    });
                    fromAll().whenAny(
                        function(state, event) {
                            return state;
                        });
                ";
                _state = @"{""count"": 0}";
            }

            [Test, Category("v8")]
            public void source_definition_is_correct()
            {
                Assert.AreEqual(500, _source.ProcessingLagOption);
                Assert.AreEqual(true, _source.ReorderEventsOption);
            }
        }

    }
}
