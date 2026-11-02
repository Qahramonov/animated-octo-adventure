using System;
using System.Collections.Generic;
using System.Numerics;
using Survival.Core;
using Survival.Gameplay;
using Survival.Networking;
using Survival.World;

namespace Survival.Server.AI
{
    /// <summary>
    /// Keeps the number of bots at min(configured, maxPlayers - realPlayers) so the total number of
    /// active participants never exceeds the server cap, and runs bot simulation within a CPU budget:
    /// bots far from every player tick at a reduced rate (distant simulation) and brain updates are
    /// spread across ticks.
    /// </summary>
    public sealed class BotManager
    {
        private static readonly string[] CallSigns =
        {
            "Nomad", "Ranger", "Drifter", "Tracker", "Scout", "Hermit", "Wanderer", "Forager",
            "Stray", "Outcast", "Pathfinder", "Castaway", "Hunter", "Lookout", "Ember", "Raven",
        };

        private readonly GameServer _server;
        private readonly List<ServerEntity> _bots = new List<ServerEntity>(128);
        private readonly List<ServerEntity> _scratch = new List<ServerEntity>(64);
        private Rng _rng;
        private int _nameCounter;
        private int _thinkCursor;

        public BotManager(GameServer server, uint seed)
        {
            _server = server;
            _rng = new Rng(seed ^ 0xB07B07u);
        }

        public int Count => _bots.Count;
        public int DesiredCount { get; private set; }
        public int FullSimCount { get; private set; }
        public IReadOnlyList<ServerEntity> Bots => _bots;

        /// <summary>Adjusts the bot population gradually (max one spawn/despawn per tick).</summary>
        public void Balance(int realPlayers)
        {
            ServerConfig cfg = _server.Config;
            DesiredCount = Math.Max(0, Math.Min(cfg.BotCount, cfg.MaxPlayers - realPlayers));
            if (_bots.Count < DesiredCount) SpawnBot();
            else if (_bots.Count > DesiredCount) RemoveBot(_bots[_bots.Count - 1]);
        }

        public void RemoveAll()
        {
            for (int i = _bots.Count - 1; i >= 0; i--) RemoveBot(_bots[i]);
        }

        private void SpawnBot()
        {
            uint seed = _rng.NextUInt();
            Vector3 pos = _server.FindSpawnPoint();
            var skillRoll = _rng.NextFloat();
            BotSkill skill = skillRoll < 0.35f ? BotSkill.Easy : skillRoll < 0.8f ? BotSkill.Normal : BotSkill.Hard;
            _nameCounter++;
            string name = CallSigns[(int)(seed % (uint)CallSigns.Length)] + "-" + (_nameCounter % 100).ToString("00");
            ServerEntity e = _server.CreateEntity(EntityType.Bot, name, pos, _rng.Range(0f, 360f));
            e.SubType = (byte)skill;
            e.Bot = new BotBrain(seed, skill);
            _bots.Add(e);
        }

        private void RemoveBot(ServerEntity e)
        {
            _bots.Remove(e);
            _server.RemoveEntity(e, DespawnReason.Removed);
        }

        /// <summary>Simulates all bots for one server tick.</summary>
        public void Tick(uint tick, float dt)
        {
            ServerConfig cfg = _server.Config;
            WorldRuntime world = _server.World;
            MovementSettings move = _server.Movement;

            // Refresh "near a player" flags a few times per second (spatial query on the shared grid)
            if (tick % 10 == 0)
            {
                FullSimCount = 0;
                foreach (ServerEntity b in _bots)
                {
                    b.Bot.NearPlayer = _server.AnyPlayerWithin(b.Move.Position, cfg.BotFullSimRadius, _scratch);
                    if (b.Bot.NearPlayer) FullSimCount++;
                }
            }

            int thinks = 0;
            int divider = Math.Max(1, cfg.DistantBotTickDivider);
            for (int i = 0; i < _bots.Count; i++)
            {
                ServerEntity b = _bots[(i + _thinkCursor) % _bots.Count];
                if (b.IsDead)
                {
                    b.DeadTime += dt;
                    if (b.DeadTime > cfg.RespawnDelaySeconds * 2f) _server.Respawn(b);
                    continue;
                }

                bool full = b.Bot.NearPlayer;
                if (!full && (tick + (uint)b.Bot.TickPhase) % (uint)divider != 0) continue;
                float stepDt = full ? dt : dt * divider;
                if (!full && thinks >= cfg.MaxBotThinksPerTick) continue; // CPU budget for distant bots
                thinks++;

                PlayerInput input = b.Bot.Think(b, world, stepDt);
                b.Yaw = input.YawDegrees;
                MovementSim.Step(ref b.Move, input, stepDt, world, move);
                _server.OnEntityMoved(b);
            }
            if (_bots.Count > 0) _thinkCursor = (_thinkCursor + 1) % _bots.Count;
        }
    }
}
