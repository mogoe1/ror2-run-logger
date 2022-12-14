using BepInEx;
using RoR2;

namespace dev.mogoe
{
    [BepInPlugin(RunLogger.PluginGUID, RunLogger.PluginName, RunLogger.PluginVersion)]
    public class RunLogger : BaseUnityPlugin
    {
        public const string PluginGUID = "dev.mogoe.RunLogger";
        public const string PluginAuthor = "mogoe";
        public const string PluginName = "RunLogger";
        public const string PluginVersion = "0.0.52";

        public const float STATS_INTERVAL = 1.0f;

        private JsonFileLogger fileLogger;
        private float nextStatsPrint = 0;
        private HoldoutZoneController holdoutZoneController;
        private BossGroup bossGroup;


        public void Awake()
        {
            Log.Init(Logger);

            On.RoR2.Run.Start += (orig, run) =>
            {
                if (LocalUserManager.GetFirstLocalUser().currentNetworkUser.isServer)
                {
                    nextStatsPrint = 0;
                    fileLogger = new JsonFileLogger(PluginVersion, 3, Logger);
                    fileLogger.logRunStart(run.gameModeIndex, run.selectedDifficulty);
                }
                orig(run);
            };

            On.RoR2.Run.BeginStage += (orig, run) =>
            {
                if (fileLogger != null)
                {
                    fileLogger.logBeginStage(RoR2.SceneCatalog.GetSceneDefForCurrentScene().sceneDefIndex);
                }
                orig(run);
            };

            On.RoR2.Run.AdvanceStage += (orig, run, sceneDef) =>
            {
                if (fileLogger != null)
                {
                    fileLogger.logAdvanceStage();
                }
                orig(run, sceneDef);
            };

            On.RoR2.PlayerCharacterMasterController.OnBodyStart += (orig, masterController) =>
            {
                if (fileLogger != null)
                {
                    fileLogger.logPlayerSpawn(masterController);
                }
                orig(masterController);
            };

            On.RoR2.Inventory.GiveItem_ItemIndex_int += (On.RoR2.Inventory.orig_GiveItem_ItemIndex_int orig, RoR2.Inventory inventory, ItemIndex index, int count) =>
            {
                RoR2.PlayerCharacterMasterController controller = inventory.gameObject.GetComponent<RoR2.PlayerCharacterMasterController>();
                if (controller != null && fileLogger != null)
                {
                    fileLogger.logItemPickup(controller, index, count);
                }
                orig(inventory, index, count);
            };

            On.RoR2.Inventory.RemoveItem_ItemIndex_int += (On.RoR2.Inventory.orig_RemoveItem_ItemIndex_int orig, RoR2.Inventory inventory, ItemIndex index, int count) =>
            {
                RoR2.PlayerCharacterMasterController controller = inventory.gameObject.GetComponent<RoR2.PlayerCharacterMasterController>();
                if (controller != null && fileLogger != null)
                {
                    fileLogger.logItemDrop(controller, index, count);
                }
                orig(inventory, index, count);
            };

            On.RoR2.ShrineBossBehavior.AddShrineStack += (orig, behaviour, interactor) =>
            {
                if (fileLogger != null)
                {
                    fileLogger.logShrineOfMountainActivated();
                }
                orig(behaviour, interactor);
            };

            RoR2.TeleporterInteraction.onTeleporterBeginChargingGlobal += (interaction) =>
            {
                holdoutZoneController = interaction.holdoutZoneController;
                if (fileLogger != null)
                {
                    fileLogger.logTeleporterStart();
                }
            };

            BossGroup.onBossGroupStartServer += (bossGroup) =>
            {
                this.bossGroup = bossGroup;
                if (fileLogger != null)
                {

                    foreach (CharacterMaster master in bossGroup.combatSquad.readOnlyMembersList)
                    {
                        fileLogger.logBossSpawn(master);
                    }
                    fileLogger.logBossUpdate(bossGroup);

                    this.bossGroup.combatSquad.onMemberAddedServer += (addedMember) =>
                    {
                        if (fileLogger != null)
                        {
                            fileLogger.logBossSpawn(addedMember);
                        }
                    };

                    this.bossGroup.combatSquad.onMemberLost += (lostMember) =>
                    {
                        if (fileLogger != null)
                        {
                            fileLogger.logBossDeath(lostMember);
                        }
                    };
                }
            };

            BossGroup.onBossGroupDefeatedServer += (bossGroup) =>
            {
                if (fileLogger != null)
                {
                    fileLogger.logBossUpdate(bossGroup);
                }
                this.bossGroup = null;
            };

            RoR2.TeleporterInteraction.onTeleporterChargedGlobal += (interaction) =>
            {
                holdoutZoneController = null;
                if (fileLogger != null)
                {
                    fileLogger.logTeleporterCharged();
                }
            };

            RoR2.TeleporterInteraction.onTeleporterFinishGlobal += (interaction) =>
            {
                if (fileLogger != null)
                {
                    fileLogger.logTeleporterFinished();
                }
            };

            On.RoR2.PlayerCharacterMasterController.OnBodyDeath += (orig, masterControler) =>
            {
                if (fileLogger != null)
                {
                    fileLogger.logPlayerDeath(masterControler);
                }
                orig(masterControler);
            };

            On.RoR2.Run.BeginGameOver += (orig, run, endingDef) =>
            {
                if (fileLogger != null)
                {
                    fileLogger.logRunEnd(endingDef.isWin);
                    foreach (NetworkUser user in RoR2.NetworkUser.readOnlyInstancesList)
                    {
                        fileLogger.logStatsUpdate(user);
                    }
                    fileLogger.Dispose();
                    fileLogger = null;
                }
                orig(run, endingDef);
            };

            Run.onRunDestroyGlobal += (Run run) =>
            {
                if (fileLogger != null)
                {
                    Logger.LogWarning("run cancled");
                    fileLogger.Dispose();
                    fileLogger = null;
                }
            };
        }

        public void FixedUpdate()
        {
            if (fileLogger == null)
            {
                return;
            }

            if (RoR2.Run.instance.time < nextStatsPrint)
            {
                return;
            }

            foreach (NetworkUser user in RoR2.NetworkUser.readOnlyInstancesList)
            {
                fileLogger.logStatsUpdate(user);
            }

            if (holdoutZoneController != null)
            {
                fileLogger.logTeleporterUpdate(holdoutZoneController);
            }

            if (bossGroup != null)
            {
                fileLogger.logBossUpdate(bossGroup);
            }

            nextStatsPrint = nextStatsPrint + STATS_INTERVAL;
        }
    }
}
