using Terraria;
using Terraria.ID;
using TerrariaApi.Server;
using TShockAPI;

namespace GameProgressSign
{
    [ApiVersion(2, 1)]
    public class GameProgressSignPlugin : TerrariaPlugin
    {
        public override string Name => "GameProgressSign";
        public override Version Version => new Version(2, 6);
        public override string Author => "Ruff Trigger";
        public override string Description => "Shows boss kill status in two permanent signs at spawn, placed side by side.";

        private static readonly SemaphoreSlim SignUpdateSemaphore = new SemaphoreSlim(1, 1);

        public GameProgressSignPlugin(Main game) : base(game) { }

        public override void Initialize()
        {
            Commands.ChatCommands.Add(new Command("GameProgressSign.use", BossStatusCommand, "bossstatus"));
            ServerApi.Hooks.NpcKilled.Register(this, OnNpcKilled);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                ServerApi.Hooks.NpcKilled.Deregister(this, OnNpcKilled);
            }
            base.Dispose(disposing);
        }

        private async void BossStatusCommand(CommandArgs args)
        {
            await UpdateSignsAsync("System");
        }

        private async void OnNpcKilled(NpcKilledEventArgs args)
        {
            if (IsBoss(args.npc) || IsInvasionEnemy(args.npc))
            {
                await UpdateSignsAsync("System");
            }
        }

        private async Task UpdateSignsAsync(string ownerName)
        {
            await SignUpdateSemaphore.WaitAsync();
            try
            {
                await PlaceOrUpdateSpawnSignsAsync(ownerName);
            }
            catch (Exception ex)
            {
                TShock.Log.Error($"[GameProgressSign] Error updating signs: {ex.Message}");
            }
            finally
            {
                SignUpdateSemaphore.Release();
            }
        }

        private async Task PlaceOrUpdateSpawnSignsAsync(string ownerName)
        {
            int y = Main.spawnTileY;
            int xStart = Main.spawnTileX - 10;
            int xSecond = xStart + 5;
            int xThird = xSecond + 5;
            // Check if signs already exist
            bool firstSignExists = SignExistsAt(xStart, y);
            bool secondSignExists = SignExistsAt(xSecond, y);
            bool thirdSignExists = SignExistsAt(xThird, y);

            //Console.Write("firstSignExists =" + firstSignExists + Environment.NewLine);
            //Console.Write("secondSignExists =" + secondSignExists + Environment.NewLine);
            //Console.Write("thirdSignExists =" + thirdSignExists + Environment.NewLine);

            if (firstSignExists && secondSignExists && thirdSignExists)
            {
                // Update text on existing signs without replacing
                await UpdateExistingSignTextAsync(xStart, y, true, false);
                await UpdateExistingSignTextAsync(xSecond, y, false, false);
                await UpdateExistingSignTextAsync(xThird, y, false, true);
                return;
            }
            else
            {
                // Only clear obstacles if signs do not already exist
                await ClearObstaclesForSignsAsync(xStart, y, xThird);
                await EnsureSolidSupportBlockAsync(xStart, y + 1);
                await EnsureSolidSupportBlockAsync(xSecond, y + 1);
                await EnsureSolidSupportBlockAsync(xThird, y + 1);

                // Place new signs if they don't exist
                await PlaceOrUpdateSingleSignAsync(ownerName, xStart, y, true, false);
                await PlaceOrUpdateSingleSignAsync(ownerName, xSecond, y, false, false);
                await PlaceOrUpdateSingleSignAsync(ownerName, xThird, y, false, true);

            }
        }

        private bool SignExistsAt(int x, int y)
        {
            return Main.tile[x, y].active() && Main.tile[x, y].type == TileID.Signs;
        }

        private async Task UpdateExistingSignTextAsync(int x, int y, bool isPreHardmode, bool isinvasion)
        {
            await Task.Run(() =>
            {
                int signIndex = Sign.ReadSign(x, y, true);
                if (signIndex >= 0 && Main.sign[signIndex] != null)
                {
                    if (isinvasion)
                    {
                        Main.sign[signIndex].text = GetInvasionText();
                    }
                    else
                    {
                        Main.sign[signIndex].text = isPreHardmode ? GetPreHardmodeText() : GetHardmodeText();
                    }

                    // Properly sync sign text to clients
                    NetMessage.SendData((int)PacketTypes.SignNew, -1, -1, null, signIndex);

                    // Ensure tile data is also synced
                    NetMessage.SendTileSquare(-1, x, y, 3, TileChangeType.None);
                }
            });
        }

        private async Task ClearObstaclesForSignsAsync(int x1, int y, int x2)
        {
            await Task.Run(() =>
            {
                for (int i = x1 - 3; i <= x2 + 3; i++)
                {
                    for (int j = y - 3; j <= y; j++)
                    {
                        Tile tile = Main.tile[i, j] as Tile;
                        if (tile == null) continue;

                        if (tile.active() && tile.type != TileID.Signs)
                        {
                            WorldGen.KillTile(i, j, false, false, true);
                            tile.wall = 0;
                        }
                    }
                }
            });
        }

        private async Task EnsureSolidSupportBlockAsync(int x, int y)
        {
            await Task.Run(() =>
            {
                WorldGen.KillTile(x, y, false, false, true);
                WorldGen.PlaceTile(x, y, TileID.WoodBlock, true, true);
                WorldGen.SquareTileFrame(x, y);
            });
        }

        private async Task PlaceOrUpdateSingleSignAsync(string ownerName, int x, int y, bool isPreHardmode, bool isinvasion)
        {
            await Task.Run(() =>
            {
                // If no sign exists, create a new one
                bool placed = WorldGen.PlaceObject(x, y, TileID.Signs, true, 0);
                WorldGen.SquareTileFrame(x, y);

                if (placed)
                {
                    int signIndex = Sign.ReadSign(x, y, true);
                    if (signIndex >= 0 && Main.sign[signIndex] != null)
                    {
                        if (isinvasion)
                        {
                            Main.sign[signIndex].text = GetInvasionText();
                        }
                        else
                        {
                            Main.sign[signIndex].text = isPreHardmode ? GetPreHardmodeText() : GetHardmodeText();
                        }
                        NetMessage.SendTileSquare(-1, x, y, 3, TileChangeType.None);
                    }
                }
            });
        }

        private bool IsBoss(NPC npc)
        {
            return npc.type == NPCID.KingSlime ||
                   npc.type == NPCID.EyeofCthulhu ||
                   npc.type == NPCID.BrainofCthulhu ||
                   npc.type == NPCID.EaterofWorldsHead ||
                   npc.type == NPCID.SkeletronHead ||
                   npc.type == NPCID.QueenBee ||
                   npc.type == NPCID.WallofFlesh ||
                   npc.type == NPCID.Retinazer ||
                   npc.type == NPCID.Spazmatism ||
                   npc.type == NPCID.TheDestroyer ||
                   npc.type == NPCID.SkeletronPrime ||
                   npc.type == NPCID.Plantera ||
                   npc.type == NPCID.Golem ||
                   npc.type == NPCID.CultistBoss ||
                   npc.type == NPCID.MoonLordCore ||
                   npc.type == NPCID.QueenSlimeBoss ||
                   npc.type == NPCID.DukeFishron ||
                   npc.type == NPCID.HallowBoss ||
                   npc.type == NPCID.Deerclops;
        }
        private bool IsInvasionEnemy(NPC npc)
        {
            return npc.type == NPCID.GoblinSorcerer || npc.type == NPCID.PirateCaptain || npc.type == NPCID.MartianSaucer || Main.invasionType > 0;
        }

        private string GetInvasionText()
        {
            return "Goblin Army: " + (NPC.downedGoblins ? "✔" : "✘") + "\n" +
                   "Pirate Invasion: " + (NPC.downedPirates ? "✔" : "✘") + "\n" +
                   "Martian Madness: " + (NPC.downedMartians ? "✔" : "✘") + "\n" +
                   "Pumpkin Moon: " + ((NPC.downedHalloweenKing && NPC.downedHalloweenTree) ? "✔" : "✘") + "\n" +
                   "Frost Moon: " + ((NPC.downedChristmasIceQueen && NPC.downedChristmasSantank && NPC.downedChristmasTree) ? "✔" : "✘") + "\n" +
                   "Celestial Pillars: " + (NPC.TowersDefeated ? "✔" : "✘") + "\n" +
                   "Frost Legion: " + (NPC.downedFrost ? "✔" : "✘");
        }

        private string GetPreHardmodeText()
        {
            return "King Slime: " + (NPC.downedSlimeKing ? "✔" : "✘") + "\n" +
                   "Eye of Cthulhu: " + (NPC.downedBoss1 ? "✔" : "✘") + "\n" +
                   "Eater of Worlds/Brain of Cthulhu: " + (NPC.downedBoss2 ? "✔" : "✘") + "\n" +
                   "Skeletron: " + (NPC.downedBoss3 ? "✔" : "✘") + "\n" +
                   "Queen Bee: " + (NPC.downedQueenBee ? "✔" : "✘") + "\n" +
                   "Deerclops: " + (NPC.downedDeerclops ? "✔" : "✘") + "\n" +
                   "Wall of Flesh: " + (Main.hardMode ? "✔" : "✘");
        }

        private string GetHardmodeText()
        {
            return "The Destroyer: " + (NPC.downedMechBoss1 ? "✔" : "✘") + "\n" +
                   "The Twins: " + (NPC.downedMechBoss2 ? "✔" : "✘") + "\n" +
                   "Skeletron Prime: " + (NPC.downedMechBoss3 ? "✔" : "✘") + "\n" +
                   "Plantera: " + (NPC.downedPlantBoss ? "✔" : "✘") + "\n" +
                   "Golem: " + (NPC.downedGolemBoss ? "✔" : "✘") + "\n" +
                   "Queen Slime: " + (NPC.downedQueenSlime ? "✔" : "✘") + "\n" +
                   "Duke Fishron: " + (NPC.downedFishron ? "✔" : "✘") + "\n" +
                   "Empress of Light: " + (NPC.downedEmpressOfLight ? "✔" : "✘") + "\n" +
                   "Lunatic Cultist: " + (NPC.downedAncientCultist ? "✔" : "✘") + "\n" +
                   "Moon Lord: " + (NPC.downedMoonlord ? "✔" : "✘");
        }
    }
}
