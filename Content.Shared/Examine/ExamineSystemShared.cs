using System.Linq;
using Content.Shared._Oxyd.Computers.Cameras;
using Content.Shared.Eye.Blinding.Components;
using Content.Shared.Ghost.Components;
using Content.Shared.Interaction;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.SurveillanceCamera.Components;
using JetBrains.Annotations;
using Robust.Shared.Containers;
using Robust.Shared.Map;
using Robust.Shared.Physics;
using Robust.Shared.Utility;
using static Content.Shared.Interaction.SharedInteractionSystem;

namespace Content.Shared.Examine
{
    public abstract partial class ExamineSystemShared : EntitySystem
    {
        [Dependency] private OccluderSystem _occluder = default!;
        [Dependency] protected SharedTransformSystem _transform = default!;
        [Dependency] private SharedContainerSystem _containerSystem = default!;
        [Dependency] private SharedInteractionSystem _interactionSystem = default!;
        [Dependency] protected MobStateSystem MobStateSystem = default!;

        [Dependency] private EntityQuery<GhostComponent> _ghostQuery = default!;
        [Dependency] private EntityQuery<OccluderComponent> _occluderQuery = default!;
        [Dependency] private EntityQuery<TransformComponent> _xformQuery = default!;
        [Dependency] private EntityQuery<ActiveCameraViewerComponent> _cameraViewerQuery = default!;
        [Dependency] private EntityQuery<SurveillanceCameraComponent> _cameraQuery = default!;

        private readonly List<RayCastResults> _occluderRaycastResults = new();

        public const float MaxRaycastRange = 100;

        /// <summary>
        ///     Examine range to use when the examiner is in critical condition.
        /// </summary>
        /// <remarks>
        ///     Detailed examinations are disabled while incapacitated. Ideally this should just be set equal to the
        ///     radius of the crit overlay that blackens most of the screen. The actual radius of that is defined
        ///     in a shader sooo... eh.
        /// </remarks>
        public const float CritExamineRange = 1.3f;

        /// <summary>
        ///     Examine range to use when the examiner is dead. See <see cref="CritExamineRange"/>.
        /// </summary>
        public const float DeadExamineRange = 0.75f;

        public const float ExamineRange = 16f;
        protected const float ExamineDetailsRange = 3f;
        protected const float CameraExamineDetailsRange = 6f;

        protected const float ExamineBlurrinessMult = 2.5f;

        /// <summary>
        ///     Creates a new examine tooltip with arbitrary info.
        /// </summary>
        public abstract void SendExamineTooltip(EntityUid player,
            EntityUid target,
            FormattedMessage message,
            bool getVerbs,
            bool centerAtCursor);

        /// <summary>
        /// Checks if an entity is close enough to an examiner to show information classified as details.
        /// Like you cannot read an ID card if the person is to far away.
        /// Or discern the nature of an held item from far away besides size of the item.
        /// </summary>
        /// <param name="examiner">The entity doing the examining.</param>
        /// <param name="entity">The entity being examined.</param>
        /// <returns>Returns true if in details range.</returns>
        public bool IsInDetailsRange(EntityUid examiner, EntityUid entity)
        {
            if (IsClientSide(entity))
                return true;

            // Ghosts can see everything.
            if (_ghostQuery.HasComp(examiner))
                return true;

            // check if the mob is in critical or dead
            if (MobStateSystem.IsIncapacitated(examiner))
                return false;

            if (!InRangeUnOccluded(examiner, entity, ExamineDetailsRange))
            {
                return IsInCameraDetailsRange(examiner, entity);
            }

            // Is the target hidden in a opaque locker or something? Currently this check allows players to examine
            // their organs, if they can somehow target them. Really this should be with userSeeInsideSelf: false, and a
            // separate check for if the item is in their inventory or hands.
            if (_containerSystem.IsInSameOrTransparentContainer(examiner, entity, userSeeInsideSelf: true))
                return true;

            // is it inside of an open storage (e.g., an open backpack)?
            return _interactionSystem.CanAccessViaStorage(examiner, entity);
        }

        protected bool IsInCameraDetailsRange(EntityUid examiner, EntityUid entity)
        {
            if (!_cameraViewerQuery.TryComp(examiner, out var viewer) || viewer.ActiveCameras.Count == 0)
                return false;

            foreach (var camNet in viewer.ActiveCameras)
            {
                if (!TryGetEntity(camNet, out var camUid) ||
                    !Exists(camUid.Value) ||
                    !_cameraQuery.TryComp(camUid.Value, out var camComp) ||
                    !camComp.Active)
                {
                    continue;
                }

                if (InRangeUnOccluded(camUid.Value, entity, CameraExamineDetailsRange, predicate: e => e == camUid.Value || e == entity) &&
                    _containerSystem.IsInSameOrTransparentContainer(camUid.Value, entity, userSeeInsideSelf: false))
                {
                    return true;
                }
            }

            return false;
        }

        protected bool IsTargetNearActiveCamera(EntityUid examiner, MapCoordinates target, float range)
        {
            if (!_cameraViewerQuery.TryComp(examiner, out var viewer) || viewer.ActiveCameras.Count == 0)
                return false;

            foreach (var camNet in viewer.ActiveCameras)
            {
                if (!TryGetEntity(camNet, out var camUid) ||
                    !Exists(camUid.Value) ||
                    !_cameraQuery.TryComp(camUid.Value, out var camComp) ||
                    !camComp.Active)
                {
                    continue;
                }

                var camCoords = _transform.GetMapCoordinates(camUid.Value);
                if (camCoords.MapId != target.MapId)
                    continue;

                if ((target.Position - camCoords.Position).Length() <= range + 0.5f)
                    return true;
            }

            return false;
        }

        protected bool CanExamineThroughActiveCamera(
            EntityUid examiner,
            MapCoordinates target,
            float range,
            Ignored? predicate = null,
            EntityUid? examined = null)
        {
            if (!_cameraViewerQuery.TryComp(examiner, out var viewer) || viewer.ActiveCameras.Count == 0)
                return false;

            foreach (var camNet in viewer.ActiveCameras)
            {
                if (!TryGetEntity(camNet, out var camUid) ||
                    !Exists(camUid.Value) ||
                    !_cameraQuery.TryComp(camUid.Value, out var camComp) ||
                    !camComp.Active)
                {
                    continue;
                }

                if (!_xformQuery.TryComp(camUid.Value, out var camXform) || camXform.MapID != target.MapId)
                    continue;

                var camOrigin = camUid.Value;
                Ignored camPred = e => e == camOrigin || e == examiner || (examined != null && e == examined.Value) || (predicate != null && predicate(e));

                if (examined != null)
                {
                    if (InRangeUnOccluded(camOrigin, examined.Value, range, predicate: camPred))
                        return true;
                }
                else
                {
                    if (InRangeUnOccluded(camOrigin, target, range, predicate: camPred))
                        return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Checks if an entity may be examined by another.
        /// </summary>
        [Pure]
        public bool CanExamine(EntityUid examiner, EntityUid examined)
        {
            // special check for client-side entities stored in null-space for some UI guff.
            if (IsClientSide(examined))
                return true;

            return !Deleted(examined) && CanExamine(examiner,
                _transform.GetMapCoordinates(examined),
                entity => entity == examiner || entity == examined,
                examined);
        }

        /// <summary>
        /// Check if an entity can examine another entity at specific coordinates.
        /// </summary>
        /// <param name="examiner">Entity doing the examining.</param>
        /// <param name="target">Coordinates of the examination.</param>
        /// <param name="predicate">Predicate to ignore entities blocking examining.</param>
        /// <param name="examined">The entity being examined, if any.</param>
        /// <param name="examinerComp">Examiner component for the examining entity.</param>
        /// <returns></returns>
        [Pure]
        public virtual bool CanExamine(EntityUid examiner,
            MapCoordinates target,
            Ignored? predicate = null,
            EntityUid? examined = null,
            ExaminerComponent? examinerComp = null)
        {
            // TODO occluded container checks
            // also requires checking if the examiner has either a storage or stripping UI open, as the item may be accessible via that UI

            if (!Resolve(examiner, ref examinerComp, false))
                return false;

            // Ghosts and admins skip examine checks.
            if (examinerComp.SkipChecks)
                return true;

            if (examined != null)
            {
                var ev = new ExamineAttemptEvent(examiner);
                RaiseLocalEvent(examined.Value, ev);
                if (ev.Cancelled)
                    return false;
            }

            if (!examinerComp.CheckInRangeUnOccluded)
                return true;

            var range = GetExaminerRange(examiner);
            if (Comp<TransformComponent>(examiner).MapID == target.MapId)
            {
                // Do target InRangeUnoccluded which has different checks.
                var directOk = examined != null
                    ? InRangeUnOccluded(examiner, examined.Value, range, predicate: predicate)
                    : InRangeUnOccluded(examiner, target, range, predicate: predicate);
                if (directOk)
                    return true;
            }

            return CanExamineThroughActiveCamera(examiner, target, range, predicate, examined);
        }

        /// <summary>
        ///     Check if a given examiner is incapacitated. If yes, return a reduced examine range. Otherwise, return the deault range.
        /// </summary>
        public float GetExaminerRange(EntityUid examiner, MobStateComponent? mobState = null)
        {
            if (Resolve(examiner, ref mobState, logMissing: false))
            {
                if (MobStateSystem.IsDead(examiner, mobState))
                    return DeadExamineRange;

                if (MobStateSystem.IsCritical(examiner, mobState) ||
                    TryComp<BlindableComponent>(examiner, out var blind) && blind.IsBlind)
                    return CritExamineRange;

                if (TryComp<BlurryVisionComponent>(examiner, out var blurry))
                    return Math.Clamp(ExamineRange - blurry.Magnitude * ExamineBlurrinessMult, 2, ExamineRange);
            }

            return ExamineRange;
        }

        /// <summary>
        /// True if occluders are drawn for this entity, otherwise false.
        /// </summary>
        public bool IsOccluded(EntityUid uid)
        {
            return TryComp<EyeComponent>(uid, out var eye) && eye.DrawFov;
        }

        /// <summary>
        /// Checks if there is clear line of sight between two points and the distance to smale.
        /// </summary>
        /// <param name="origin">Origin coordinates.</param>
        /// <param name="other">Target coordinates</param>
        /// <param name="range">Maximum range</param>
        /// <param name="predicate">If a blocking entity evaluates for true, the entity is ignored.</param>
        /// <returns>Returns true if no occlusion was found between origin and other and both are in range.</returns>
        public bool InRangeUnOccluded(MapCoordinates origin,
            MapCoordinates other,
            float range,
            Ignored? predicate)
        {
            // No, rider. This is better.
            // ReSharper disable once ConvertToLocalFunction
            var wrapped = (EntityUid uid, Ignored? wrapped)
                => wrapped != null && wrapped(uid);

            return InRangeUnOccluded(origin, other, range, predicate, wrapped);
        }

        /// <summary>
        /// Checks if there is clear line of sight between two points and the distance to smale.
        /// </summary>
        /// <param name="origin">Origin coordinates.</param>
        /// <param name="other">Target coordinates</param>
        /// <param name="range">Maximum range</param>
        /// <param name="state">if a blocking entity evaluates for true, the entity is ignored.</param>
        /// <param name="predicate">if a blocking entity evaluates for true, the entity is ignored.</param>
        /// <returns>Returns true if no occlusion was found between origin and other and both are in range.</returns>
        public bool InRangeUnOccluded<TState>(MapCoordinates origin,
            MapCoordinates other,
            float range,
            TState state,
            Func<EntityUid, TState, bool> predicate)
        {
            if (other.MapId != origin.MapId ||
                other.MapId == MapId.Nullspace)
                return false;

            var dir = other.Position - origin.Position;
            var length = dir.Length();

            // If range specified also check it
            // TODO: This rounding check is here because the API is kinda eh
            if (range > 0f && length > range + 0.01f)
                return false;

            if (MathHelper.CloseTo(length, 0))
                return true;

            if (length > MaxRaycastRange)
            {
                Log.Warning("InRangeUnOccluded check performed over extreme range. Limiting CollisionRay size.");
                length = MaxRaycastRange;
            }

            var ray = new Ray(origin.Position, dir.Normalized());
            var rayResults = _occluderRaycastResults;
            _occluder.IntersectRay(rayResults, origin.MapId, ray, length);

            if (rayResults.Count == 0)
                return true;

            foreach (var result in rayResults)
            {
                if (predicate(result.HitEntity, state))
                    continue;

                if (!_occluderQuery.TryComp(result.HitEntity, out var occluder) ||
                    !_xformQuery.TryComp(result.HitEntity, out var xform))
                {
                    return false;
                }

                if (_occluder.ContainsPoint(occluder, xform, origin.Position) ||
                    _occluder.ContainsPoint(occluder, xform, other.Position))
                {
                    continue;
                }

                return false;
            }

            return true;
        }

        /// <summary>
        /// Checks if there is clear line of sight between to entities.
        /// </summary>
        /// <param name="origin">The entity doing examination</param>
        /// <param name="other">Target of the examination</param>
        /// <param name="range">Maximum range for the examination</param>
        /// <param name="predicate">if a blocking entity evaluates for true, the entity is ignored.</param>
        /// <returns>Returns true if no occlusion was found between origin and other and both are in range.</returns>
        public bool InRangeUnOccluded(EntityUid origin,
            EntityUid other,
            float range = ExamineRange,
            Ignored? predicate = null)
        {
            var ev = new InRangeOverrideEvent(origin, other);
            RaiseLocalEvent(origin, ref ev);

            if (ev.Handled)
            {
                return ev.InRange;
            }

            var originPos = _transform.GetMapCoordinates(origin);
            var otherPos = _transform.GetMapCoordinates(other);

            return InRangeUnOccluded(originPos, otherPos, range, predicate);
        }

        /// <summary>
        /// Checks if there is clear line of sight between an  entity and some space.
        /// </summary>
        /// <param name="origin">The entity doing examination</param>
        /// <param name="other">Target of the examination</param>
        /// <param name="range">Maximum range for the examination</param>
        /// <param name="predicate">if a blocking entity evaluates for true, the entity is ignored.</param>
        /// <returns>Returns true if no occlusion was found between origin and other and both are in range.</returns>
        public bool InRangeUnOccluded(EntityUid origin,
            EntityCoordinates other,
            float range = ExamineRange,
            Ignored? predicate = null)
        {
            var originPos = _transform.GetMapCoordinates(origin);
            var otherPos = _transform.ToMapCoordinates(other);

            return InRangeUnOccluded(originPos, otherPos, range, predicate);
        }

        /// <summary>
        /// Checks if there is clear line of sight between an entity and some space.
        /// </summary>
        /// <param name="origin">The entity doing examination</param>
        /// <param name="other">Target space of the examination</param>
        /// <param name="range">Maximum range for the examination</param>
        /// <param name="predicate">if a blocking entity evaluates for true, the entity is ignored.</param>
        /// <returns></returns>
        public bool InRangeUnOccluded(EntityUid origin,
            MapCoordinates other,
            float range = ExamineRange,
            Ignored? predicate = null)
        {
            var originPos = _transform.GetMapCoordinates(origin);

            return InRangeUnOccluded(originPos, other, range, predicate);
        }

        /// <summary>
        /// Generate the examine message for a pair of examiner and an entity.
        /// </summary>
        public FormattedMessage GetExamineText(EntityUid entity, EntityUid? examiner)
        {
            var message = new FormattedMessage();

            if (examiner == null)
            {
                return message;
            }

            var hasDescription = false;
            var metadata = MetaData(entity);

            //Add an entity description if one is declared
            if (!string.IsNullOrEmpty(metadata.EntityDescription))
            {
                message.AddMarkupOrThrow(metadata.EntityDescription);
                hasDescription = true;
            }

            message.PushColor(Color.DarkGray);

            // Raise the event and let things that subscribe to it change the message...
            var isInDetailsRange = IsInDetailsRange(examiner.Value, entity);
            var examinedEvent = new ExaminedEvent(message, entity, examiner.Value, isInDetailsRange, hasDescription);
            RaiseLocalEvent(entity, examinedEvent);

            var newMessage = examinedEvent.GetTotalMessage();

            // pop color tag
            newMessage.Pop();

            return newMessage;
        }
    }

    /// <summary>
    ///     Raised when an entity is examined.
    ///     If you're pushing multiple messages that should be grouped together (or ordered in some way),
    ///     call <see cref="PushGroup"/> before pushing and <see cref="PopGroup"/> when finished.
    /// </summary>
    public sealed class ExaminedEvent : EntityEventArgs
    {
        /// <summary>
        ///     The message that will be displayed as the examine text.
        ///     You should use <see cref="PushMarkup"/> and similar instead to modify this,
        ///     since it handles newlines/priority and such correctly.
        /// </summary>
        /// <seealso cref="PushMessage"/>
        /// <seealso cref="PushMarkup"/>
        /// <seealso cref="PushText"/>
        /// <seealso cref="AddMessage"/>
        /// <seealso cref="AddMarkup"/>
        /// <seealso cref="AddText"/>
        private FormattedMessage Message { get; }

        /// <summary>
        ///     Parts of the examine message that will later be sorted by priority and pushed onto <see cref="Message"/>.
        /// </summary>
        private List<ExamineMessagePart> Parts { get; } = new();

        /// <summary>
        ///     Whether the examiner is in range of the entity to get some extra details.
        /// </summary>
        public bool IsInDetailsRange { get; }

        /// <summary>
        ///     The entity performing the examining.
        /// </summary>
        public EntityUid Examiner { get; }

        /// <summary>
        ///     Entity being examined, for broadcast event purposes.
        /// </summary>
        public EntityUid Examined { get; }

        private bool _hasDescription;

        private ExamineMessagePart? _currentGroupPart;

        public ExaminedEvent(FormattedMessage message,
            EntityUid examined,
            EntityUid examiner,
            bool isInDetailsRange,
            bool hasDescription)
        {
            Message = message;
            Examined = examined;
            Examiner = examiner;
            IsInDetailsRange = isInDetailsRange;
            _hasDescription = hasDescription;
        }

        /// <summary>
        ///     Returns <see cref="Message"/> with all <see cref="Parts"/> appended according to their priority.
        /// </summary>
        public FormattedMessage GetTotalMessage()
        {
            int Comparison(ExamineMessagePart a, ExamineMessagePart b)
            {
                // Try sort by priority, then group, then by string contents
                if (a.Priority != b.Priority)
                {
                    // negative so that expected behavior is consistent with what makes sense
                    // i.e. a negative priority should mean its at the bottom of the list, right?
                    return -a.Priority.CompareTo(b.Priority);
                }

                if (a.Group != b.Group)
                {
                    return string.Compare(a.Group, b.Group, StringComparison.Ordinal);
                }

                return string.Compare(a.Message.ToString(), b.Message.ToString(), StringComparison.Ordinal);
            }

            // tolist/clone formatted message so calling this multiple times wont fuck shit up
            // (if that happens for some reason)
            var parts = Parts.ToList();
            var totalMessage = new FormattedMessage(Message);
            parts.Sort(Comparison);

            if (_hasDescription && parts.Count > 0)
            {
                totalMessage.PushNewline();
            }

            foreach (var part in parts)
            {
                totalMessage.AddMessage(part.Message);
                if (part.DoNewLine && parts.Last() != part)
                    totalMessage.PushNewline();
            }

            totalMessage.TrimEnd();

            return totalMessage;
        }

        /// <summary>
        ///     Message group handling. Call this if you want the next set of examine messages that you're adding to have
        ///     a consistent order with regards to each other. This is done so that client & server will always
        ///     sort messages the same as well as grouped together properly, even if subscriptions are different.
        ///     You should wrap it in a using() block so popping automatically occurs.
        /// </summary>
        public ExamineGroupDisposable PushGroup(string groupName, int priority = 0)
        {
            // Ensure that other examine events correctly ended their groups.
            DebugTools.Assert(_currentGroupPart == null);
            _currentGroupPart = new ExamineMessagePart(new FormattedMessage(), priority, false, groupName);
            return new ExamineGroupDisposable(this);
        }

        /// <summary>
        ///     Ends the current group and pushes its groups contents to the message.
        ///     This will be called automatically if in using a `using` block with <see cref="PushGroup"/>.
        /// </summary>
        private void PopGroup()
        {
            DebugTools.Assert(_currentGroupPart != null);
            if (_currentGroupPart != null && !_currentGroupPart.Message.IsEmpty)
            {
                Parts.Add(_currentGroupPart);
            }

            _currentGroupPart = null;
        }

        /// <summary>
        /// Push another message into this examine result, on its own line.
        /// End message will be grouped by <see cref="priority"/>, then by group if one was started
        /// then by ordinal comparison.
        /// </summary>
        /// <seealso cref="PushMarkup"/>
        /// <seealso cref="PushText"/>
        public void PushMessage(FormattedMessage message, int priority = 0)
        {
            if (message.Nodes.Count == 0)
                return;

            if (_currentGroupPart != null)
            {
                message.PushNewline();
                _currentGroupPart.Message.AddMessage(message);
            }
            else
            {
                Parts.Add(new ExamineMessagePart(message, priority, true, null));
            }
        }

        /// <summary>
        /// Push another message parsed from markup into this examine result, on its own line.
        /// End message will be grouped by <see cref="priority"/>, then by group if one was started
        /// then by ordinal comparison.
        /// </summary>
        /// <seealso cref="PushText"/>
        /// <seealso cref="PushMessage"/>
        public void PushMarkup(string markup, int priority = 0)
        {
            PushMessage(FormattedMessage.FromMarkupOrThrow(markup), priority);
        }

        /// <summary>
        /// Push another message containing raw text into this examine result, on its own line.
        /// End message will be grouped by <see cref="priority"/>, then by group if one was started
        /// then by ordinal comparison.
        /// </summary>
        /// <seealso cref="PushMarkup"/>
        /// <seealso cref="PushMessage"/>
        public void PushText(string text, int priority = 0)
        {
            var msg = new FormattedMessage();
            msg.AddText(text);
            PushMessage(msg, priority);
        }

        /// <summary>
        /// Adds a message directly without starting a newline after.
        /// End message will be grouped by <see cref="priority"/>, then by group if one was started
        /// then by ordinal comparison.
        /// </summary>
        /// <seealso cref="AddMarkup"/>
        /// <seealso cref="AddText"/>
        public void AddMessage(FormattedMessage message, int priority = 0)
        {
            if (message.Nodes.Count == 0)
                return;

            if (_currentGroupPart != null)
            {
                _currentGroupPart.Message.AddMessage(message);
            }
            else
            {
                Parts.Add(new ExamineMessagePart(message, priority, false, null));
            }
        }

        /// <summary>
        /// Adds markup directly without starting a newline after.
        /// End message will be grouped by <see cref="priority"/>, then by group if one was started
        /// then by ordinal comparison.
        /// </summary>
        /// <seealso cref="AddText"/>
        /// <seealso cref="AddMessage"/>
        public void AddMarkup(string markup, int priority = 0)
        {
            AddMessage(FormattedMessage.FromMarkupOrThrow(markup), priority);
        }

        /// <summary>
        /// Adds text directly without starting a newline after.
        /// End message will be grouped by <see cref="priority"/>, then by group if one was started
        /// then by ordinal comparison.
        /// </summary>
        /// <seealso cref="AddMarkup"/>
        /// <seealso cref="AddMessage"/>
        public void AddText(string text, int priority = 0)
        {
            var msg = new FormattedMessage();
            msg.AddText(text);
            AddMessage(msg, priority);
        }

        public struct ExamineGroupDisposable : IDisposable
        {
            private ExaminedEvent _event;

            public ExamineGroupDisposable(ExaminedEvent @event)
            {
                _event = @event;
            }

            public void Dispose()
            {
                _event.PopGroup();
            }
        }

        private record ExamineMessagePart(FormattedMessage Message, int Priority, bool DoNewLine, string? Group);
    }


    /// <summary>
    ///     Event raised directed at an entity that someone is attempting to examine
    /// </summary>
    public sealed class ExamineAttemptEvent : CancellableEntityEventArgs
    {
        public readonly EntityUid Examiner;

        public ExamineAttemptEvent(EntityUid examiner)
        {
            Examiner = examiner;
        }
    }
}
