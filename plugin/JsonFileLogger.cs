using BepInEx.Logging;
using RoR2;
using RoR2.Skills;
using RoR2.Stats;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace dev.mogoe
{
    class JsonFileLogger : IDisposable
    {
        StreamWriter streamWriter;
        ManualLogSource _logger;
        QueuedLock queuedLock;

        public JsonFileLogger(string modVersion, int logVersion, ManualLogSource logger)
        {
            queuedLock = new QueuedLock();
            string folderPath = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDoc‌​uments), "ror2-run-logger");
            string filePath = System.IO.Path.Combine(folderPath, $"{DateTime.Now.Year}-{DateTime.Now.Month}-{DateTime.Now.Day}-{DateTime.Now.Hour}-{DateTime.Now.Minute}-{DateTime.Now.Second}.json");
            System.IO.Directory.CreateDirectory(folderPath);
            FileStream fileStream = new FileStream(filePath,
                                       FileMode.Append,
                                       FileAccess.Write,
                                       FileShare.ReadWrite);
            streamWriter = new StreamWriter(fileStream);

            this._logger = logger;
            this._logger.LogInfo("started logging to " + filePath);
            this.write("{", prependNewLine: false, prependComma: false);
            this.write($"   \"timestamp\": {DateTimeOffset.Now.ToUnixTimeSeconds()},", prependComma: false);
            this.write($"   \"modVersion\": \"{modVersion}\",", prependComma: false);
            this.write($"   \"logVersion\": \"{logVersion}\",", prependComma: false);
            this.write($"   \"log\":[", flush: true, prependComma: false);
        }

        private void write(string entry, bool prependComma = true, bool flush = false, bool prependNewLine = true)
        {
            try
            {
                queuedLock.Enter();
                if (prependComma)
                {
                    streamWriter.Write(",");
                }
                if (prependNewLine)
                {
                    streamWriter.Write(Environment.NewLine);
                }
                streamWriter.Write(entry.Replace("'", "\""));

                if (flush)
                {
                    streamWriter.Flush();
                }
            }
            finally
            {

                queuedLock.Exit();
            }
        }
        private Task writeAsync(string entry, bool prependComma = true, bool flush = false, bool prependNewLine = true)
        {
            return Task.Run(() => write(entry, prependComma, flush, prependNewLine));
        }

        private string formatDecimal(double number)
        {
            return $"{number:0.####}".Replace(",", ".");
        }
        private string getTime()
        {
            return formatDecimal(RoR2.Run.instance.time);
        }

        private string getStopwatch()
        {
            return formatDecimal(RoR2.Run.instance.GetRunStopwatch());
        }

        private string getStat(StatSheet sheet, StatDef def)
        {
            return formatDecimal(sheet.GetStatValueAsDouble(def));
        }

        private string getChargeAmount(HoldoutZoneController controller)
        {
            return formatDecimal(controller.charge);
        }
        public void logRunStart(GameModeIndex gameModeIndex, DifficultyIndex difficulty)
        {
            var buffer = $"      {{'type':'RUN_START', 'time':{getTime()}, 'stopwatch':{getStopwatch()}, 'gameModeIndex':{gameModeIndex}, 'hostedBy':'{LocalUserManager.GetFirstLocalUser().userProfile.name}', 'difficulty':{(int)difficulty}}}";
            this.writeAsync(buffer, prependComma: false);
        }

        public void logBeginStage(SceneIndex sceneIndex)
        {
            var buffer = $"      {{'type':'STAGE_START', 'time':{getTime()}, 'stopwatch':{getStopwatch()}, 'stageIndex':{sceneIndex}, 'difficultyCoeff':{formatDecimal(Run.instance.difficultyCoefficient)}}}";
            this.writeAsync(buffer);
        }

        public void logAdvanceStage()
        {
            var buffer = $"      {{'type':'STAGE_FINISHED', 'time':{getTime()}, 'stopwatch':{getStopwatch()}, 'difficultyCoeff':{formatDecimal(Run.instance.difficultyCoefficient)}}}";
            this.writeAsync(buffer);
        }

        public void logPlayerSpawn(PlayerCharacterMasterController masterController)
        {
            var survirvor = SurvivorCatalog.FindSurvivorDefFromBody(masterController.master.bodyPrefab);
            var buffer = $"      {{'type':'PLAYER_SPAWN', 'time':{getTime()}, 'stopwatch':{getStopwatch()}, 'playerId':'{masterController.networkUser.id.value}', 'playerName':'{masterController.GetDisplayName()}', 'survivorId':{(int)survirvor.survivorIndex}, 'survivorName':'{survirvor.cachedName}', 'loadout':{getLoadoutAsJson(masterController)}}}";
            this.writeAsync(buffer);
        }

        private string getLoadoutAsJson(PlayerCharacterMasterController masterController)
        {
            var bodyIndex = masterController.master.GetBody().bodyIndex;
            var loadoutManager = masterController.master.loadout.bodyLoadoutManager;
            var bodySkills = BodyCatalog.GetBodyPrefabSkillSlots(bodyIndex);

            List<string> entries = new List<string>();

            for (var i = 0; i < bodySkills.Length; i++)
            {
                var familyName = bodySkills[i].skillName;

                var usedVariantIndex = loadoutManager.GetSkillVariant(bodyIndex, i);
                var usedVariant = bodySkills[i].skillFamily.variants[usedVariantIndex];
                var usedVariantCatalogIndex = usedVariant.skillDef.skillIndex;
                entries.Add($"{{'family':'{familyName}', 'variant':'{SkillCatalog.GetSkillName(usedVariantCatalogIndex)}'}}");                    
            }
            return "[" + String.Join(", ", entries) + "]";
        }

        public void logPlayerDeath(PlayerCharacterMasterController masterController)
        {
            var buffer = $"      {{'type':'PLAYER_DEATH', 'time':{getTime()}, 'stopwatch':{getStopwatch()}, 'playerId':'{masterController.networkUser.id.value}', 'playerName':'{masterController.GetDisplayName()}'}}";
            this.writeAsync(buffer);
        }

        public void logItemPickup(PlayerCharacterMasterController masterController, ItemIndex itemIndex, int amount)
        {
            if (amount == 0)
            {
                return;
            }
            var playerId = masterController?.networkUser?.id.value;
            if (playerId == null)
            {
                this._logger.LogWarning($"ITEM_PICKUP (ItemIndex {itemIndex}) without playerId, ignoring.");
            }

            var buffer = $"      {{'type':'ITEM_PICKUP', 'time':{getTime()}, 'stopwatch':{getStopwatch()}, 'playerId':'{playerId}', 'playerName':'{masterController.GetDisplayName()}', 'itemId':{itemIndex}, 'itemName':'{ItemCatalog.GetItemDef(itemIndex).name}', 'count':{amount}}}";
            this.writeAsync(buffer);
        }

        public void logItemDrop(PlayerCharacterMasterController masterController, ItemIndex itemIndex, int amount)
        {
            if (amount == 0)
            {
                return;
            }
            var buffer = $"      {{'type':'ITEM_DROP', 'time':{getTime()}, 'stopwatch':{getStopwatch()}, 'playerId':'{masterController.networkUser.id.value}', 'playerName':'{masterController.GetDisplayName()}', 'itemId':{itemIndex}, 'itemName':'{ItemCatalog.GetItemDef(itemIndex).name}', 'count':{amount}}}";
            this.writeAsync(buffer);
        }

        public void logShrineOfMountainActivated()
        {
            var buffer = $"      {{'type':'MOUNTAIN_SHRINE_ACTIVATED', 'time':{getTime()}, 'stopwatch':{getStopwatch()}}}";
            this.writeAsync(buffer);
        }

        public void logBossSpawn(CharacterMaster characterMaster)
        {
            var buffer = $"      {{'type':'BOSS_SPAWN', 'time':{getTime()}, 'stopwatch':{getStopwatch()}, 'bossId':{(int)characterMaster.masterIndex}, 'bossName':'{MasterCatalog.GetMasterPrefab(characterMaster.masterIndex).name}'}}";
            this.writeAsync(buffer);
        }

        public void logBossDeath(CharacterMaster characterMaster)
        {
            var buffer = $"      {{'type':'BOSS_DEATH', 'time':{getTime()}, 'stopwatch':{getStopwatch()}, 'bossId':{(int)characterMaster.masterIndex}, 'bossName':'{MasterCatalog.GetMasterPrefab(characterMaster.masterIndex).name}'}}";
            this.writeAsync(buffer);
        }

        public void logBossUpdate(BossGroup bossGroup)
        {
            var numBossesAlive = bossGroup.combatSquad.memberCount;
            float totalHealth = 0;
            for (int i = 0; i < numBossesAlive; i++)
            {
                totalHealth += bossGroup.combatSquad.readOnlyMembersList[i].GetBody()?.healthComponent?.health ?? 0.0f;
            }
            var buffer = $"      {{'type':'BOSS_UPDATE', 'time':{getTime()}, 'stopwatch':{getStopwatch()}, 'numBossesAlive':{numBossesAlive}, 'totalBossesHealth':{this.formatDecimal(totalHealth)}}}";
            this.writeAsync(buffer);
        }

        public void logTeleporterStart()
        {
            var buffer = $"      {{'type':'TELEPORTER_START', 'time':{getTime()}, 'stopwatch':{getStopwatch()}}}";
            this.writeAsync(buffer);
        }

        public void logTeleporterCharged()
        {
            var buffer = $"      {{'type':'TELEPORTER_CHARGED', 'time':{getTime()}, 'stopwatch':{getStopwatch()}}}";
            this.writeAsync(buffer);
        }


        public void logTeleporterUpdate(HoldoutZoneController zoneController)
        {
            if (zoneController != null)
            {
                var buffer = $"      {{'type':'TELEPORTER_UPDATE', 'time':{getTime()}, 'stopwatch':{getStopwatch()}, 'chargedAmount':{getChargeAmount(zoneController)}}}";
                this.writeAsync(buffer);
            }
        }

        public void logTeleporterFinished()
        {
            var buffer = $"      {{'type':'TELEPORTER_FINISHED', 'time':{getTime()}, 'stopwatch':{getStopwatch()}}}";
            this.writeAsync(buffer);
        }

        public void logRunEnd(bool isWin)
        {
            var buffer = $"      {{'type':'RUN_END', 'time':{getTime()}, 'stopwatch':{getStopwatch()}, 'isWin':{(isWin ? "true" : "false")}}}";
            this.writeAsync(buffer, flush: true);
        }

        public void logStatsUpdate(NetworkUser user)
        {
            if (user?.master?.GetBody()?.healthComponent == null)
            {
                return;
            }

            StatSheet sheet = PlayerStatsComponent.FindMasterStatSheet(user.master);
            if (sheet != null)
            {
                HealthComponent.HealthBarValues healthBar = user.master.GetBody().healthComponent.GetHealthBarValues();
                var buffer = $"      {{'type':'STATS_UPDATE', 'time':{getTime()}, 'stopwatch':{getStopwatch()}, 'playerId':'{user.id.value}', 'playerName':'{user.masterController.GetDisplayName()}'," +
                    $"'totalDamageDealt':{getStat(sheet, StatDef.totalDamageDealt)}, " +
                    $"'totalMinionDamageDealt':{getStat(sheet, StatDef.totalMinionDamageDealt)}, " +
                    $"'totalKills':{getStat(sheet, StatDef.totalKills)}, " +
                    $"'totalMinionKills':{getStat(sheet, StatDef.totalMinionKills)}, " +
                    $"'highestDamageDealt':{getStat(sheet, StatDef.highestDamageDealt)}, " +
                    $"'totalDamageTaken':{getStat(sheet, StatDef.totalDamageTaken)}, " +
                    $"'goldCollected':{getStat(sheet, StatDef.goldCollected)}, " +
                    $"'totalGoldPurchases':{getStat(sheet, StatDef.totalGoldPurchases)}, " +
                    $"'currentHealthFraction':{formatDecimal(healthBar.healthFraction)}, " +
                    $"'currentShieldFraction':{formatDecimal(healthBar.shieldFraction)}, " +
                    $"'currentBarrierFraction':{formatDecimal(healthBar.barrierFraction)}" +
                    $"}}";
                this.writeAsync(buffer);
            }
        }

        public void Dispose()
        {
            lock (streamWriter)
            {
                this.write("   ]", prependComma: false);
                this.write("}", flush: true, prependComma: false);
                streamWriter.Close();
                this._logger.LogInfo("finished logging");
            }
        }
    }

    public sealed class QueuedLock
    {
        private object innerLock;
        private volatile int ticketsCount = 0;
        private volatile int ticketToRide = 1;

        public QueuedLock()
        {
            innerLock = new Object();
        }

        public void Enter()
        {
            int myTicket = Interlocked.Increment(ref ticketsCount);
            Monitor.Enter(innerLock);
            while (true)
            {

                if (myTicket == ticketToRide)
                {
                    return;
                }
                else
                {
                    Monitor.Wait(innerLock);
                }
            }
        }

        public void Exit()
        {
            Interlocked.Increment(ref ticketToRide);
            Monitor.PulseAll(innerLock);
            Monitor.Exit(innerLock);
        }
    }
}
