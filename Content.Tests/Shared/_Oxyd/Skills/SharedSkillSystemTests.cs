using System;
using Content.Shared._Oxyd.Skills;
using Moq;
using NUnit.Framework;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.Manager;
using Robust.Shared.Timing;

namespace Content.Tests.Shared._Oxyd.Skills;

[TestFixture, TestOf(typeof(SharedSkillSystem))]
public sealed class SharedSkillSystemTests : ContentUnitTest
{
    private const string SkillId = "SkillTest";

    private IEntityManager _entityManager = default!;
    private IPrototypeManager _prototypeManager = default!;
    private TestSkillSystem _skillSystem = default!;
    private TimeSpan _currentTime;

    protected override Type[] ExtraComponents => new[] { typeof(MobSkillComponent) };

    [OneTimeSetUp]
    public void OneTimeSetup()
    {
        IoCManager.Resolve<ISerializationManager>().Initialize();
        _prototypeManager = IoCManager.Resolve<IPrototypeManager>();
        _prototypeManager.Initialize();
        _prototypeManager.LoadString($"""
            - type: Skill
              id: {SkillId}
              name: Test skill
              description: Test skill
            """);
        _prototypeManager.ResolveResults();

        _entityManager = IoCManager.Resolve<IEntityManager>();

        var timing = new Mock<IGameTiming>();
        timing.SetupGet(value => value.CurTime).Returns(() => _currentTime);

        _skillSystem = new TestSkillSystem();
        _skillSystem.Configure((EntityManager) _entityManager, _prototypeManager, timing.Object);
    }

    [SetUp]
    public void Setup()
    {
        _currentTime = TimeSpan.Zero;
    }

    [Test]
    public void SetUniqueBuff_KeepsExistingAmountAndExpiration()
    {
        var entity = CreateSkillEntity();
        try
        {
            var original = _skillSystem.SetUniqueBuff(entity, "litany", 2, SkillId, TimeSpan.FromSeconds(5));
            var updated = _skillSystem.SetUniqueBuff(entity, "litany", 7, SkillId, TimeSpan.FromSeconds(20));

            using (Assert.EnterMultipleScope())
            {
                Assert.That(updated, Is.SameAs(original));
                Assert.That(updated, Is.SameAs(entity.Comp.buffSources[SkillId]["litany"][0]));
                Assert.That(updated.amount, Is.EqualTo(2));
                Assert.That(updated.expires, Is.EqualTo(TimeSpan.FromSeconds(5)));
                Assert.That(entity.Comp.skills[SkillId][1], Is.EqualTo(2));
                Assert.That(entity.Comp.buffSources[SkillId]["litany"], Has.Count.EqualTo(1));
            }
        }
        finally
        {
            _entityManager.DeleteEntity(entity.Owner);
        }
    }

    [Test]
    public void SetUniqueBuff_RefreshesExpirationForMatchingAmountAndExpires()
    {
        var entity = CreateSkillEntity();
        try
        {
            _currentTime = TimeSpan.FromSeconds(10);
            var initial = _skillSystem.SetUniqueBuff(entity, "litany", 3, SkillId, TimeSpan.FromSeconds(5));

            _currentTime = TimeSpan.FromSeconds(12);
            var refreshed = _skillSystem.SetUniqueBuff(entity, "litany", 3, SkillId, TimeSpan.FromSeconds(5));

            using (Assert.EnterMultipleScope())
            {
                Assert.That(refreshed, Is.SameAs(initial));
                Assert.That(refreshed.expires, Is.EqualTo(TimeSpan.FromSeconds(17)));
                Assert.That(entity.Comp.skills[SkillId][1], Is.EqualTo(3));
            }

            _currentTime = TimeSpan.FromSeconds(18);
            _skillSystem.Update(0f);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(entity.Comp.buffSources[SkillId]["litany"], Is.Empty);
                Assert.That(entity.Comp.skills[SkillId][1], Is.Zero);
            }
        }
        finally
        {
            _entityManager.DeleteEntity(entity.Owner);
        }
    }

    [Test]
    public void RemoveBuff_RecalculatesAfterMatchingEntryIsRemoved()
    {
        var entity = CreateSkillEntity();
        try
        {
            var first = _skillSystem.AddBuff(entity, "first", 3, SkillId);
            var second = _skillSystem.AddBuff(entity, "second", 5, SkillId);
            Assert.That(entity.Comp.skills[SkillId][1], Is.EqualTo(8));

            _skillSystem.RemoveBuff(entity, SkillId, "first", first);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(entity.Comp.buffSources[SkillId]["first"], Is.Empty);
                Assert.That(entity.Comp.buffSources[SkillId]["second"][0], Is.SameAs(second));
                Assert.That(entity.Comp.skills[SkillId][1], Is.EqualTo(5));
            }
        }
        finally
        {
            _entityManager.DeleteEntity(entity.Owner);
        }
    }

    [Test]
    public void RemoveBuff_NoOpLeavesEntryAndTotalUnchanged()
    {
        var entity = CreateSkillEntity();
        try
        {
            var existing = _skillSystem.AddBuff(entity, "litany", 6, SkillId);
            var absent = new MobSkillComponent.BuffData { amount = 100 };

            _skillSystem.RemoveBuff(entity, SkillId, "litany", absent);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(entity.Comp.buffSources[SkillId]["litany"], Has.Count.EqualTo(1));
                Assert.That(entity.Comp.buffSources[SkillId]["litany"][0], Is.SameAs(existing));
                Assert.That(entity.Comp.skills[SkillId][1], Is.EqualTo(6));
            }
        }
        finally
        {
            _entityManager.DeleteEntity(entity.Owner);
        }
    }

    private Entity<MobSkillComponent> CreateSkillEntity()
    {
        var uid = _entityManager.Spawn();
        var component = _entityManager.AddComponent<MobSkillComponent>(uid);
        component.skills[SkillId] = new[] { 0, 0 };
        return (uid, component);
    }

    private sealed class TestSkillSystem : SharedSkillSystem
    {
        public void Configure(EntityManager entityManager, IPrototypeManager prototypeManager, IGameTiming timing)
        {
            EntityManager = entityManager;
            ProtoMan = prototypeManager;
            this.timing = timing;
        }
    }
}
