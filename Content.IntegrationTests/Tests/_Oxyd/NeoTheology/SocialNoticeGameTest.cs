using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Content.IntegrationTests.Fixtures;
using Content.Shared._Oxyd.NeoTheology.Effects;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._Oxyd.NeoTheology;

public abstract class SocialNoticeGameTest : GameTest
{
    private LitanyEffectSystem _noticeEffects = default!;
    private Action<EntityUid, string> _observer = default!;

    public override async Task DoSetup()
    {
        await base.DoSetup();
        await Server.WaitPost(() =>
        {
            _noticeEffects = SEntMan.System<LitanyEffectSystem>();
            var notices = new Dictionary<EntityUid, List<string>>();
            SocialNoticeTestCapture.Captures.Add(_noticeEffects, notices);
            _observer = (recipient, message) =>
            {
                if (!notices.TryGetValue(recipient, out var messages))
                    notices[recipient] = messages = new List<string>();
                messages.Add(message);
            };
            _noticeEffects.SocialNotice += _observer;
        });
    }

    public override async Task DoTeardown()
    {
        await Server.WaitPost(() =>
        {
            _noticeEffects.SocialNotice -= _observer;
            SocialNoticeTestCapture.Captures.Remove(_noticeEffects);
        });
        await base.DoTeardown();
    }
}

internal static class SocialNoticeTestCapture
{
    internal static readonly ConditionalWeakTable<LitanyEffectSystem, Dictionary<EntityUid, List<string>>> Captures = new();

    public static void TestingClearSocialNotices(this LitanyEffectSystem effects) => Captures.GetOrCreateValue(effects).Clear();

    public static IReadOnlyList<string> TestingGetSocialNotices(this LitanyEffectSystem effects, EntityUid recipient)
    {
        return Captures.TryGetValue(effects, out var notices) && notices.TryGetValue(recipient, out var messages)
            ? messages
            : Array.Empty<string>();
    }
}
