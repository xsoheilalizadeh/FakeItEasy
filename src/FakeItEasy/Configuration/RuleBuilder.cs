namespace FakeItEasy.Configuration
{
    using System;
    using System.Collections.Generic;
    using System.Linq.Expressions;
#if FEATURE_NETCORE_REFLECTION
    using System.Reflection;
#endif
    using FakeItEasy.Core;

    internal partial class RuleBuilder
        : IAnyCallConfigurationWithVoidReturnType,
          IAfterCallConfiguredWithOutAndRefParametersConfiguration<IVoidConfiguration>,
          IThenConfiguration<IVoidConfiguration>
    {
        private readonly FakeAsserter.Factory asserterFactory;
        private readonly FakeManager manager;
        private bool wasRuleAdded;

        internal RuleBuilder(BuildableCallRule ruleBeingBuilt, FakeManager manager, FakeAsserter.Factory asserterFactory)
        {
            this.RuleBeingBuilt = ruleBeingBuilt;
            this.manager = manager;
            this.asserterFactory = asserterFactory;
        }

        /// <summary>
        /// Represents a delegate that creates a configuration object from
        /// a fake object and the rule to build.
        /// </summary>
        /// <param name="ruleBeingBuilt">The rule that's being built.</param>
        /// <param name="fakeObject">The fake object the rule is for.</param>
        /// <returns>A configuration object.</returns>
        internal delegate RuleBuilder Factory(BuildableCallRule ruleBeingBuilt, FakeManager fakeObject);

        public BuildableCallRule RuleBeingBuilt { get; }

        IVoidConfiguration IThenConfiguration<IVoidConfiguration>.Then => this.Then;

        public IEnumerable<CompletedFakeObjectCall> Calls => this.manager.GetRecordedCalls();

        public ICallMatcher Matcher => new RuleMatcher(this);

        private RuleBuilder Then
        {
            get
            {
                var newRule = this.RuleBeingBuilt.CloneCallSpecification();
                return new RuleBuilder(newRule, this.manager, this.asserterFactory) { PreviousRule = this.RuleBeingBuilt };
            }
        }

        private BuildableCallRule PreviousRule { get; set; }

        public IThenConfiguration<IVoidConfiguration> NumberOfTimes(int numberOfTimes)
        {
            if (numberOfTimes <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(numberOfTimes),
                    numberOfTimes,
                    "The number of times to occur is not greater than zero.");
            }

            this.RuleBeingBuilt.NumberOfTimesToCall = numberOfTimes;
            return this;
        }

        public virtual IAfterCallConfiguredConfiguration<IVoidConfiguration> Throws(Func<IFakeObjectCall, Exception> exceptionFactory)
        {
            this.AddRuleIfNeeded();
            this.RuleBeingBuilt.UseApplicator(call =>
            {
                Exception exceptionToThrow;
                try
                {
                    exceptionToThrow = exceptionFactory(call);
                }
                catch (Exception ex) when (!(ex is FakeConfigurationException))
                {
                    throw new UserCallbackException(ExceptionMessages.UserCallbackThrewAnException("Exception factory"), ex);
                }

                throw exceptionToThrow;
            });
            return this;
        }

        public IAfterCallConfiguredConfiguration<IVoidConfiguration> Throws<T>() where T : Exception, new() =>
            this.Throws<IVoidConfiguration, T>();

        public IVoidConfiguration WhenArgumentsMatch(Func<ArgumentCollection, bool> argumentsPredicate)
        {
            Guard.AgainstNull(argumentsPredicate, nameof(argumentsPredicate));

            this.RuleBeingBuilt.UsePredicateToValidateArguments(argumentsPredicate);
            return this;
        }

        public virtual IAfterCallConfiguredConfiguration<IVoidConfiguration> DoesNothing()
        {
            this.AddRuleIfNeeded();
            this.RuleBeingBuilt.UseDefaultApplicator();
            return this;
        }

        public virtual IVoidConfiguration Invokes(Action<IFakeObjectCall> action)
        {
            Guard.AgainstNull(action, nameof(action));
            this.AddRuleIfNeeded();
            this.RuleBeingBuilt.Actions.Add(action);
            return this;
        }

        public virtual IAfterCallConfiguredConfiguration<IVoidConfiguration> CallsBaseMethod()
        {
            if (this.manager.FakeObjectType.GetTypeInfo().IsSubclassOf(typeof(Delegate)))
            {
                throw new FakeConfigurationException(ExceptionMessages.DelegateCannotCallBaseMethod);
            }

            this.AddRuleIfNeeded();
            this.RuleBeingBuilt.UseApplicator(x => { });
            this.RuleBeingBuilt.CallBaseMethod = true;
            return this;
        }

        public virtual IAfterCallConfiguredConfiguration<IVoidConfiguration> AssignsOutAndRefParametersLazily(Func<IFakeObjectCall, ICollection<object>> valueProducer)
        {
            Guard.AgainstNull(valueProducer, nameof(valueProducer));

            this.AddRuleIfNeeded();
            this.RuleBeingBuilt.OutAndRefParametersValueProducer = valueProducer;

            return this;
        }

        [Obsolete("Assertions using the Repeated class will be removed in version 6.0.0. Use other variants of MustHaveHappened instead.")]
        public UnorderedCallAssertion MustHaveHappened(Repeated repeatConstraint)
        {
            Guard.AgainstNull(repeatConstraint, nameof(repeatConstraint));

            return this.MustHaveHappened(repeatConstraint.ToCallCountConstraint());
        }

        public UnorderedCallAssertion MustHaveHappened(int numberOfTimes, Times timesOption)
        {
            Guard.AgainstNull(timesOption, nameof(timesOption));

            return this.MustHaveHappened(timesOption.ToCallCountConstraint(numberOfTimes));
        }

        public UnorderedCallAssertion MustHaveHappenedANumberOfTimesMatching(Expression<Func<int, bool>> predicate)
        {
            Guard.AgainstNull(predicate, nameof(predicate));

            return this.MustHaveHappened(new CallCountConstraint(predicate.Compile(), $"a number of times matching the predicate '{predicate}'"));
        }

        public IAnyCallConfigurationWithVoidReturnType Where(Func<IFakeObjectCall, bool> predicate, Action<IOutputWriter> descriptionWriter)
        {
            this.RuleBeingBuilt.ApplyWherePredicate(predicate, descriptionWriter);
            return this;
        }

        private UnorderedCallAssertion MustHaveHappened(CallCountConstraint callCountConstraint)
        {
            var asserter = this.asserterFactory.Invoke(this.Calls, this.manager.GetLastRecordedSequenceNumber());

            asserter.AssertWasCalled(this.Matcher.Matches, this.RuleBeingBuilt.WriteDescriptionOfValidCall, callCountConstraint);

            return new UnorderedCallAssertion(this.manager, this.Matcher, this.RuleBeingBuilt.WriteDescriptionOfValidCall, callCountConstraint);
        }

        private void AddRuleIfNeeded()
        {
            if (!this.wasRuleAdded)
            {
                if (this.PreviousRule != null)
                {
                    this.manager.AddRuleAfter(this.PreviousRule, this.RuleBeingBuilt);
                }
                else
                {
                    this.manager.AddRuleFirst(this.RuleBeingBuilt);
                }

                this.wasRuleAdded = true;
            }
        }

        public partial class ReturnValueConfiguration<TMember>
            : IAnyCallConfigurationWithReturnTypeSpecified<TMember>,
              IAfterCallConfiguredWithOutAndRefParametersConfiguration<IReturnValueConfiguration<TMember>>,
              IThenConfiguration<IReturnValueConfiguration<TMember>>
        {
            public ReturnValueConfiguration(RuleBuilder parentConfiguration)
            {
                this.ParentConfiguration = parentConfiguration;
            }

            public ICallMatcher Matcher => this.ParentConfiguration.Matcher;

            public IReturnValueConfiguration<TMember> Then =>
                new ReturnValueConfiguration<TMember>(this.ParentConfiguration.Then);

            public IEnumerable<ICompletedFakeObjectCall> Calls => this.ParentConfiguration.Calls;

            private RuleBuilder ParentConfiguration { get; }

            public IAfterCallConfiguredConfiguration<IReturnValueConfiguration<TMember>> Throws(Func<IFakeObjectCall, Exception> exceptionFactory)
            {
                this.ParentConfiguration.Throws(exceptionFactory);
                return this;
            }

            public IAfterCallConfiguredConfiguration<IReturnValueConfiguration<TMember>> Throws<T>() where T : Exception, new() =>
                this.Throws<IReturnValueConfiguration<TMember>, T>();

            public IAfterCallConfiguredWithOutAndRefParametersConfiguration<IReturnValueConfiguration<TMember>> ReturnsLazily(Func<IFakeObjectCall, TMember> valueProducer)
            {
                Guard.AgainstNull(valueProducer, nameof(valueProducer));
                this.ParentConfiguration.AddRuleIfNeeded();
                this.ParentConfiguration.RuleBeingBuilt.UseApplicator(call =>
                {
                    TMember returnValue;
                    try
                    {
                        returnValue = valueProducer(call);
                    }
                    catch (Exception ex) when (!(ex is FakeConfigurationException))
                    {
                        throw new UserCallbackException(ExceptionMessages.UserCallbackThrewAnException("Return value producer"), ex);
                    }

                    call.SetReturnValue(returnValue);
                });
                return this;
            }

            public IReturnValueConfiguration<TMember> Invokes(Action<IFakeObjectCall> action)
            {
                this.ParentConfiguration.Invokes(action);
                return this;
            }

            public IAfterCallConfiguredConfiguration<IReturnValueConfiguration<TMember>> CallsBaseMethod()
            {
                this.ParentConfiguration.CallsBaseMethod();
                return this;
            }

            public IReturnValueConfiguration<TMember> WhenArgumentsMatch(Func<ArgumentCollection, bool> argumentsPredicate)
            {
                this.ParentConfiguration.WhenArgumentsMatch(argumentsPredicate);
                return this;
            }

            [Obsolete("Assertions using the Repeated class will be removed in version 6.0.0. Use other variants of MustHaveHappened instead.")]
            public UnorderedCallAssertion MustHaveHappened(Repeated repeatConstraint) =>
                this.ParentConfiguration.MustHaveHappened(repeatConstraint);

            public UnorderedCallAssertion MustHaveHappened(int numberOfTimes, Times timesOption)
            {
                Guard.AgainstNull(timesOption, nameof(timesOption));

                return this.ParentConfiguration.MustHaveHappened(numberOfTimes, timesOption);
            }

            public UnorderedCallAssertion MustHaveHappenedANumberOfTimesMatching(Expression<Func<int, bool>> predicate)
            {
                Guard.AgainstNull(predicate, nameof(predicate));

                return this.ParentConfiguration.MustHaveHappenedANumberOfTimesMatching(predicate);
            }

            public IAnyCallConfigurationWithReturnTypeSpecified<TMember> Where(Func<IFakeObjectCall, bool> predicate, Action<IOutputWriter> descriptionWriter)
            {
                this.ParentConfiguration.RuleBeingBuilt.ApplyWherePredicate(predicate, descriptionWriter);
                return this;
            }

            public IAfterCallConfiguredConfiguration<IReturnValueConfiguration<TMember>> AssignsOutAndRefParametersLazily(Func<IFakeObjectCall, ICollection<object>> valueProducer)
            {
                this.ParentConfiguration.AssignsOutAndRefParametersLazily(valueProducer);
                return this;
            }

            public IThenConfiguration<IReturnValueConfiguration<TMember>> NumberOfTimes(int numberOfTimes)
            {
                this.ParentConfiguration.NumberOfTimes(numberOfTimes);
                return this;
            }
        }

        private class RuleMatcher
            : ICallMatcher
        {
            private readonly RuleBuilder builder;

            public RuleMatcher(RuleBuilder builder)
            {
                this.builder = builder;
            }

            public bool Matches(IFakeObjectCall call)
            {
                Guard.AgainstNull(call, nameof(call));

                return this.builder.RuleBeingBuilt.IsApplicableTo(call) &&
                       ReferenceEquals(this.builder.manager.Object, call.FakedObject);
            }

            public override string ToString() =>
                this.builder.RuleBeingBuilt.ToString();
        }
    }
}
