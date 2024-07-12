using System.Linq;
using Content.Server.Chat.Managers;
using Content.Server.Chat.Systems;
using Content.Server.Fax;
using Content.Server.GameTicking;
using Content.Server.Mind;
using Content.Server.Objectives;
using Content.Shared.Fax.Components;
using Content.Shared.Humanoid;
using Content.Shared.Mind.Components;
using Content.Shared.Movement.Systems;
using Content.Shared.Paper;
using Content.Shared.Roles;
using Robust.Shared.Physics.Components;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Content.Server._Sunrise.Fugitive
{
    public sealed class FugitiveSystem : EntitySystem
    {
        private const string AntagRole = "Fugitive";
        private const string EscapeObjective = "FugitiveEscapeShuttleObjective";
        private const string GameRule = "Fugitive";

        [Dependency] private readonly IPrototypeManager _prototypeManager = default!;
        [Dependency] private readonly MovementSpeedModifierSystem _movementSpeed = default!;
        [Dependency] private readonly IGameTiming _timing = default!;
        [Dependency] private readonly ChatSystem _chat = default!;
        [Dependency] private readonly MindSystem _mindSystem = default!;
        [Dependency] private readonly FaxSystem _faxSystem = default!;
        [Dependency] private readonly IChatManager _chatManager = default!;
        [Dependency] private readonly SharedRoleSystem _roleSystem = default!;
        [Dependency] private readonly GameTicker _gameTicker = default!;


        public override void Initialize()
        {
            base.Initialize();
            SubscribeLocalEvent<FugitiveComponent, MindAddedMessage>(OnMindAdded);

            SubscribeLocalEvent<FugitiveRuleComponent, ObjectivesTextGetInfoEvent>(OnObjectivesTextGetInfo);
        }

        private void OnObjectivesTextGetInfo(EntityUid uid, FugitiveRuleComponent comp, ref ObjectivesTextGetInfoEvent args)
        {
            args.Minds = comp.FugitiveMinds;
            args.AgentName = Loc.GetString("fugitive-round-end-name");
        }

        public override void Update(float frameTime)
        {
            base.Update(frameTime);
            foreach (var (cd, _) in EntityQuery<FugitiveCountdownComponent, _Sunrise.Fugitive.FugitiveComponent>())
            {
                if (cd.AnnounceTime == null || !(_timing.CurTime > cd.AnnounceTime))
                    continue;
                _chat.DispatchGlobalAnnouncement(Loc.GetString("station-event-fugitive-hunt-announcement"), sender: Loc.GetString("fugitive-announcement-GALPOL"), colorOverride: Color.Yellow);

                SendFugiReport(cd.Owner);

                RemCompDeferred<FugitiveCountdownComponent>(cd.Owner);
            }
        }

        public bool SendFugiReport(EntityUid fugitive)
        {
            var report = GenerateFugiReport(fugitive);
            var faxes = EntityManager.EntityQuery<FaxMachineComponent>();
            var wasSent = false;
            foreach (var fax in faxes)
            {
                if (!fax.ReceiveNukeCodes)
                {
                    continue;
                }

                var printout = new FaxPrintout(
                    report.ToString(),
                    Loc.GetString("fugi-report-ent-name", ("name", fugitive)),
                    null,
                    null,
                    "paper_stamp-centcom",
                    new List<StampDisplayInfo>
                    {
                        new StampDisplayInfo { StampedName = Loc.GetString("stamp-component-stamped-name-centcom"), StampedColor = Color.FromHex("#BB3232") },
                    });
                _faxSystem.Receive(fax.Owner, printout, null, fax);

                wasSent = true;
            }

            return wasSent;
        }

        private void OnMindAdded(EntityUid uid, FugitiveComponent component, MindAddedMessage args)
        {
            var fugitiveRule = EntityQuery<FugitiveRuleComponent>().FirstOrDefault();
            if (fugitiveRule == null)
            {
                //todo fuck me this shit is awful
                //no i wont fuck you, erp is against rules
                _gameTicker.StartGameRule(GameRule, out var ruleEntity);
                fugitiveRule = Comp<FugitiveRuleComponent>(ruleEntity);
            }

            if (!_mindSystem.TryGetMind(uid, out var mindId, out var mind))
            {
                return;
            }

            if (component.FirstMindAdded)
                return;

            component.FirstMindAdded = true;


            if (_roleSystem.MindHasRole<FugitiveRoleComponent>(mindId))
            {
                _roleSystem.MindTryRemoveRole<FugitiveRoleComponent>(mindId);
            }

            _roleSystem.MindAddRole(mindId, new FugitiveRoleComponent
            {
                PrototypeId = AntagRole
            });

            if (_mindSystem.TryGetSession(mind, out var session))
            {
                _chatManager.DispatchServerMessage(session, Loc.GetString("fugitive-role-greeting"));
            }

            _mindSystem.TryAddObjective(mindId, mind, EscapeObjective);

            fugitiveRule.FugitiveMinds.Add((mindId, Name(uid)));

            // workaround seperate shitcode moment
            _movementSpeed.RefreshMovementSpeedModifiers(uid);
        }

        private FormattedMessage GenerateFugiReport(EntityUid uid)
        {
            FormattedMessage report = new();
            report.AddMarkup(Loc.GetString("fugi-report-title", ("name", uid)));
            report.PushNewline();
            report.PushNewline();
            report.AddMarkup(Loc.GetString("fugitive-report-first-line", ("name", uid)));
            report.PushNewline();
            report.PushNewline();


            if (!TryComp<HumanoidAppearanceComponent>(uid, out var humanoidComponent) ||
                !_prototypeManager.TryIndex(humanoidComponent.Species, out var species))
            {
                report.AddMarkup(Loc.GetString("fugitive-report-inhuman", ("name", uid)));
                return report;
            }

            report.AddMarkup(Loc.GetString("fugitive-report-morphotype", ("species", Loc.GetString(species.Name))));
            report.PushNewline();
            report.AddMarkup(Loc.GetString("fugitive-report-age", ("age", humanoidComponent.Age)));
            report.PushNewline();

            string sexLine = string.Empty;
            sexLine += humanoidComponent.Sex switch
            {
                Sex.Male => Loc.GetString("fugitive-report-sex-m"),
                Sex.Female => Loc.GetString("fugitive-report-sex-f"),
                _ => Loc.GetString("fugitive-report-sex-n")
            };

            report.AddMarkup(sexLine);

            if (TryComp<PhysicsComponent>(uid, out var physics))
            {
                report.PushNewline();
                report.AddMarkup(Loc.GetString("fugitive-report-weight", ("weight", Math.Round(physics.FixturesMass))));
            }
            report.PushNewline();
            report.PushNewline();
            report.AddMarkup(Loc.GetString("fugitive-report-last-line"));

            return report;
        }
    }
}