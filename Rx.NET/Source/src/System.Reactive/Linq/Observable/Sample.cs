﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information. 

using System.Reactive.Concurrency;
using System.Reactive.Disposables;

namespace System.Reactive.Linq.ObservableImpl
{
    internal sealed class Sample<TSource, TSample> : Producer<TSource, Sample<TSource, TSample>._>
    {
        private readonly IObservable<TSource> _source;
        private readonly IObservable<TSample> _sampler;

        public Sample(IObservable<TSource> source, IObservable<TSample> sampler)
        {
            _source = source;
            _sampler = sampler;
        }

        protected override _ CreateSink(IObserver<TSource> observer) => new _(observer);

        protected override void Run(_ sink) => sink.Run(this);

        internal sealed class _ : IdentitySink<TSource>
        {
            private readonly object _gate = new object();

            public _(IObserver<TSource> observer)
                : base(observer)
            {
            }

            private IDisposable _sourceSubscription;
            private IDisposable _samplerSubscription;

            private bool _hasValue;
            private TSource _value;
            private bool _sourceAtEnd;
            private bool _samplerAtEnd;

            public void Run(Sample<TSource, TSample> parent)
            {
                var sourceSubscription = new SingleAssignmentDisposable();
                _sourceSubscription = sourceSubscription;
                sourceSubscription.Disposable = parent._source.SubscribeSafe(this);

                var samplerSubscription = new SingleAssignmentDisposable();
                _samplerSubscription = samplerSubscription;
                samplerSubscription.Disposable = parent._sampler.SubscribeSafe(new SampleObserver(this));

                SetUpstream(StableCompositeDisposable.Create(_sourceSubscription, _samplerSubscription));
            }

            public override void OnNext(TSource value)
            {
                lock (_gate)
                {
                    _hasValue = true;
                    _value = value;
                }
            }

            public override void OnError(Exception error)
            {
                lock (_gate)
                {
                    ForwardOnError(error);
                }
            }

            public override void OnCompleted()
            {
                lock (_gate)
                {
                    _sourceAtEnd = true;

                    if (_samplerAtEnd)
                        ForwardOnCompleted();
                    else
                        _sourceSubscription.Dispose();
                }
            }

            private sealed class SampleObserver : IObserver<TSample>
            {
                private readonly _ _parent;

                public SampleObserver(_ parent)
                {
                    _parent = parent;
                }

                public void OnNext(TSample value)
                {
                    lock (_parent._gate)
                    {
                        if (_parent._hasValue)
                        {
                            _parent._hasValue = false;
                            _parent.ForwardOnNext(_parent._value);
                        }

                        if (_parent._sourceAtEnd)
                        {
                            _parent.ForwardOnCompleted();
                        }
                    }
                }

                public void OnError(Exception error)
                {
                    // BREAKING CHANGE v2 > v1.x - This error used to be swallowed
                    lock (_parent._gate)
                    {
                        _parent.ForwardOnError(error);
                    }
                }

                public void OnCompleted()
                {
                    lock (_parent._gate)
                    {
                        _parent._samplerAtEnd = true;

                        if (_parent._hasValue)
                        {
                            _parent._hasValue = false;
                            _parent.ForwardOnNext(_parent._value);
                        }

                        if (_parent._sourceAtEnd)
                        {
                            _parent.ForwardOnCompleted();
                        }
                        else
                            _parent._samplerSubscription.Dispose();
                    }
                }
            }
        }
    }

    internal sealed class Sample<TSource> : Producer<TSource, Sample<TSource>._>
    {
        private readonly IObservable<TSource> _source;
        private readonly TimeSpan _interval;
        private readonly IScheduler _scheduler;

        public Sample(IObservable<TSource> source, TimeSpan interval, IScheduler scheduler)
        {
            _source = source;
            _interval = interval;
            _scheduler = scheduler;
        }

        protected override _ CreateSink(IObserver<TSource> observer) => new _(observer);

        protected override void Run(_ sink) => sink.Run(this);

        internal sealed class _ : IdentitySink<TSource>
        {
            private object _gate = new object();

            public _(IObserver<TSource> observer)
                : base(observer)
            {
            }

            private IDisposable _sourceSubscription;

            private bool _hasValue;
            private TSource _value;
            private bool _atEnd;

            public void Run(Sample<TSource> parent)
            {
                var sourceSubscription = new SingleAssignmentDisposable();
                _sourceSubscription = sourceSubscription;
                sourceSubscription.Disposable = parent._source.SubscribeSafe(this);

                SetUpstream(StableCompositeDisposable.Create(
                    sourceSubscription,
                    parent._scheduler.SchedulePeriodic(parent._interval, Tick)
                ));
            }

            private void Tick()
            {
                lock (_gate)
                {
                    if (_hasValue)
                    {
                        _hasValue = false;
                        ForwardOnNext(_value);
                    }

                    if (_atEnd)
                    {
                        ForwardOnCompleted();
                    }
                }
            }

            public override void OnNext(TSource value)
            {
                lock (_gate)
                {
                    _hasValue = true;
                    _value = value;
                }
            }

            public override void OnError(Exception error)
            {
                lock (_gate)
                {
                    ForwardOnError(error);
                }
            }

            public override void OnCompleted()
            {
                lock (_gate)
                {
                    _atEnd = true;
                    _sourceSubscription.Dispose();
                }
            }
        }
    }
}
