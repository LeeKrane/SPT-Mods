# SPT mod inventory

Generated: 2026-11-15. Source: installed files only (assembly metadata, no logs, nothing loaded).

Regenerate: `scripts/inventory.sh all` (local client + read-only rsync of `*.dll`/`package.json`/`mod.json` from taragarmr). Single targets: `scripts/inventory.sh local|bepinex <dir>|server <dir>`. Needs `dotnet` 10 on PATH or `nix` (see `scripts/inventory/README.md`).

Symlinked krane plugins show `(symlink @ <hash>)`: last repo commit touching that plugin dir. Server mod versions come from the `IModMetadata` ctor; "(assembly)" means the version is computed at runtime and was taken from the assembly info instead. Plugin tables omit dlls without `[BepInPlugin]` (libraries); `plugins-disabled/` is not scanned.

Server table checked against `journalctl --user -u spt-server` "Mod:" lines (58 mods logged): all identical except WTT-ContentBackport (file 2.0.7, log 2.0.5) and Infected Variants (file 1.3.0, log 1.2.1), both installed after the last server start. The Vagabond server (`~/spt-server-vagabond`) is currently absent and not inventoried.

## Server (taragarmr main, `~/spt-server/SPT_Runtime/user/mods`)
| Folder/File | Name | GUID | Version | Author | SPT target |
|---|---|---|---|---|---|
| acidphantasm-bosseshavegpcoins/acidphantasm-bosseshavegpcoins.dll | Bosses Have GP Coins | com.acidphantasm.bosseshavegpcoins | 1.1.0 | acidphantasm | ~4.1.0 |
| acidphantasm-bosseshavelegamedals/acidphantasm-bosseshavelegamedals.dll | Bosses Have Lega Medals | com.acidphantasm.bosseshavelegamedals | 2.1.0 | acidphantasm | ~4.1.0 |
| acidphantasm-botplacementsystem/acidphantasm-botplacementsystem.dll | Acid's Bot Placement System | com.acidphantasm.botplacementsystem | 2.1.0 | acidphantasm | ~4.1.0 |
| acidphantasm-defaultfiremode/acidphantasm-defaultfiremode.dll | Default Firemode Fixer | com.acidphantasm.defaultfiremode | 2.1.0 | acidphantasm | ~4.1.0 |
| acidphantasm-harryhideout/acidphantasm-harryhideout.dll | Harry Hideout | com.acidphantasm.harryhideout | 3.1.2 | acidphantasm | ~4.1.0 |
| acidphantasm-itemlimitsbegone/acidphantasm-itemlimitsbegone.dll | Item Limits Begone | com.acidphantasm.itemlimitsbegone | 1.1.0 | acidphantasm | ~4.1.0 |
| acidphantasm-itemvaluation/acidphantasm-itemvaluation.dll | Item Valuation | com.acidphantasm.itemvaluation | 2.1.0 | acidphantasm | ~4.1.0 |
| acidphantasm-progressivebotsystem/acidphantasm-progressivebotsystem.dll | Acid's Progressive Bot System | com.acidphantasm.progressivebotsystem | 2.4.1 | acidphantasm | ~4.1.0 |
| acidphantasm-reffriendlyquests/acidphantasm-reffriendlyquests.dll | Ref Friendly Quests | com.acidphantasm.reffriendlyquests | 2.1.0 | acidphantasm | ~4.1.0 |
| acidphantasm-scorpion/acidphantasm-scorpion.dll | Scorpion | com.acidphantasm.scorpion | 1.1.1 | acidphantasm | ~4.1.0 |
| acidphantasm-stattrack/acidphantasm-stattrack.dll | StatTrack™ | com.acidphantasm.stattrack | 2.1.1 | acidphantasm | ~4.1.0 |
| AllQuestsCheckmarks/AllQuestsCheckmarks-Server.dll | All Quests Checkmarks | com.zgfuedkx.allquestscheckmarks | 1.4.0 | ZGFueDkx | ~4.1.0 |
| archangelwtf-contentbackportprestiges/ContentBackportPrestigesServer.dll | Content Backport - Prestiges | wtf.archangel.contentbackportprestiges | 1.1.0 | ArchangelWTF | ~4.1 |
| BackpackGunSlot/BackpackGunSlot.dll | BackpackGunSlot | com.20fpsguy.backpackgunslot | 1.1.1 | 20fpsguy | ~4.1.0 |
| betterkeys/betterkeys.dll | BetterKeysNG | com.flir.betterkeys | 1.8.0 | flir | ~4.1.0 |
| BetterSuppressors/BetterSuppressors.dll | BetterSuppressors | com.tominocz.bettersuppressors | 1.0.0 (assembly) | TominoCZ | ~4.1.5 |
| BlackDivServer/BlackDivServer.dll | Black Division [REDACTED] Home | com.blackdiv.tacticaltoaster | 1.3.1 | TacticalToaster | ~4.1.5 |
| BotCallsigns/BotCallsigns.dll | Bot Callsigns | com.harmonyzt.botcallsigns | 2.0.5 | harmony | ~4.1.1 |
| CraftQueue/maschine-CraftQueue.Server.dll | CraftQueue | com.maschine.CraftQueue | 2.4.2 | maschine | ~4.1.0 |
| CSGasServer/CSGasServer.dll | CSGas | com.manimal.csgas | 2.0.0 | Manimal | ~4.1.5 |
| DrakiaXYZ-GildedKeyStorage/DrakiaXYZ-GildedKeyStorage-Server.dll | Gilded Key Storage | xyz.drakia.gildedkeystorage | 2.1.1 | DrakiaXYZ | ~4.1.0 |
| DrakiaXYZ-LiveFleaPrices/DrakiaXYZ-LiveFleaPrices.dll | Live Flea Prices | xyz.drakia.livefleaprices | 2.1.0 | DrakiaXYZ | ~4.1.0 |
| ExpandedTaskText/ExpandedTaskText.dll | Expanded Task Text | com.friedengineer.ett | 2.1.0 | Cj, FriedEngineer | ~4.1 |
| ExtraSpecialSlots/BlackHawk-ExtraSpecialSlots.Server.dll | ExtraSpecialSlots | com.blackhawk.extraspecialslots | 1.1.0 | BLACK HAWK | >=4.1.6 <4.1.7 |
| fika-server/FikaServer.dll | server | Fika | 2.4.2 | Fika | ~4.1.3 |
| GunsmithTrader/GunsmithTrader.dll | Gunsmith Trader | com.landon.gunsmithtrader | 1.0.2 | Landon | ~4.1.0 |
| HideoutWeDeserveTimeRP/HideoutWeDeserveTimeRP.dll | Hideout We Deserve - TimeRP | com.garon.hideoutwedeservetimerp | 1.4.0 | HeroicLarvy | ~4.1.0 |
| Increase Climb Height/IncreaseClimbHeight.dll | Increase Climb Height | redlaser42.IncreaseClimbHeight | 2.1.1 | redlaser42 | ~4.1.0 |
| ISB-Aishi/Aishi.dll | ISB Aishi | com.samc137.aishi | 2.0.4 | SamC137 | ~4.1.6 |
| ISB-SpecialForces/ISBSpecialForces.dll | ISB Special Forces | com.samc137.isbspecialforces | 2.0.1 | SamC137 | ~4.1.6 |
| KeepMeAlive/KeepMeAlive.Server.dll | KeepMeAlive Server | KeepMeAliveServer | 1.1.0 | awnova | ~4.1.0 |
| krane-AishiInsuranceDiscount4Server/krane.AishiInsuranceDiscount4Server.dll | Aishi Insurance Discount | krane.aishiinsurancediscount | 1.0.0 | krane | ~4.1.6 |
| krane-FleaCategoryBlock4Server/krane.FleaCategoryBlock4Server.dll | Flea Category Block | krane.fleacategoryblockserver | 1.0.0 | krane | ~4.1.6 |
| krane-FleaOfferGuard4Server/krane.FleaOfferGuard4Server.dll | Flea Offer Guard | krane.fleaofferguardserver | 1.0.0 | krane | ~4.1.6 |
| krane-InfectedVariants4Server/krane.InfectedVariants4Server.dll | Infected Variants | krane.infectedvariantsserver | 1.3.0 | krane | ~4.1.6 |
| krane-ItemTweaks4Server/krane.ItemTweaks4Server.dll | Krane's Item Tweaks & Additions (Server) | krane.itemtweaks4server | 1.0.0 | krane | ~4.1.6 |
| krane-PrestigeDogtagFix4Server/krane.PrestigeDogtagFix4Server.dll | Prestige Dogtag Fix | krane.prestigedogtagfix4server | 1.0.0 | krane | ~4.1.6 |
| krane-StorylineCleanup4Server/krane.StorylineCleanup4Server.dll | Storyline Cleanup | krane.storylinecleanup4server | 1.0.0 | krane | ~4.1.6 |
| LacyPvETweaks/LacyPvETweaks.dll | Lacyway's PvE Tweaks | com.lacyway.lpt | 1.3.2 | Lacyway | >=4.1.3 |
| Ledger/Ledger.dll | Ledger | com.zantetsukenar3.ledger | 1.1.0 | ZantetsukenAR3 | ~4.1.0 |
| LennoxP90-FactoryClassic/FactoryClassic.Server.dll | FactoryClassic | com.lennoxp90.factoryclassic | 1.2.0 | LennoxP90 | ~4.1.0 |
| LennoxP90-MapVariants/MapVariants.Server.dll | MapVariants | com.lennoxp90.mapvariants | 1.1.0 | LennoxP90 | ~4.1.0 |
| Lotus/Lotus.dll | Lotus | com.Luna.LunnayalunaLotus | 1.8.1 | LunnayalunaLotus | ~4.1.6 |
| Manimal-Halloween2026/Manimal.Halloween2026.Server.dll | Halloween2026 | com.manimal.halloween2026 | 1.0.0 | Manimal | >=4.1.6 <4.2.0 |
| ManimalIcebreaker/icebreaker-server.dll | ManimalIcebreaker | com.manimal.icebreaker | 1.1.3 | Manimal | ~4.1.5 |
| ManimalInterchange/ManimalInterchange.Server.dll | ManimalInterchangeRework | com.manimal.interchange | 1.0.8 | Manimal | ~4.1.5 |
| ManimalLabsBoiler/ManimalLabsBoilerServer.dll | ManimalLabsBoiler | com.manimal.labsboiler | 2.0.0 | Manimal | ~4.1.3 |
| ManimalLighthouse/ManimalLighthouse.Server.dll | ManimalLighthouseRework | com.manimal.lighthouse | 1.0.7 | Manimal | ~4.1.5 |
| ManimalTerminal/terminal-server.dll | ManimalTerminal | com.manimal.terminal | 0.2.20 | Manimal | ~4.1.0 |
| Michael-NATOQuartermaster/NATOQuartermaster.dll | NATO Quartermaster | com.michael.spt.natoquartermaster | 1.1.3 | Michael | ~4.1.3 |
| MoreBotsServer/MoreBotsServer.dll | MoreBotsAPI | com.morebotsapi.tacticaltoaster | 2.1.1 | TacticalToaster | ~4.1.5 |
| mpstark-dynamicmaps/mpstark-dynamicmaps.dll | Dynamic Maps | com.mpstark.dynamicmaps | 1.2.1 | mpstark | ~4.1.0 |
| ORBIT/Orbit.Server.dll | ORBIT Server | com.chazut.orbit.server | 2.1.1 | Chazut | ~4.1.0 |
| ozen.InstantInsurance/ozen.InstantInsurance.dll | Instant Insurance | com.ozen.instantinsurance | 1.1.2 | ozen | ~4.1.2 |
| PastPresentEventSelector/PastPresentEventSelector.dll | Past & Present Event Selector | com.zezinho.pastpresenteventselector | 1.7.7 | zezinho | ~4.1.6 |
| platinum-theblacklist/TheBlacklist.dll | The Blacklist | com.platinum.theblacklist | 3.1.1 | Platinum | ~4.1 |
| QuestCodex/QuestCodex.dll | QuestCodex | com.viper.questcodex | 1.11.1 (assembly) | Viper-9 | ~4.1.2 |
| RaidReview/RaidReview.dll | Raid Review | ekky.raidreview | 1.8.0 | Ekky | ~4.1.0 |
| RandomizzatoreMoreCases-4.0/RandomizzatoreMoreCases.dll | MoreCases | com.randomizzatore.morecases | 3.0.0 | Randomizzatore | ~4.1.0 |
| ReceiveAllChats/maschine-ReceiveAllChats.Server.dll | ReceiveAllChats | com.maschine.ReceiveAllChats | 2.0.1 | maschine | ~4.1.0 |
| RefExtraQuests/RefExtraQuests.dll | Ref Extra Quests | com.praedlab.refextraquests | 1.9.0 | PraED_TV | ~4.1.0 |
| RoguesVRaidersServer/RoguesVRaidersServer.dll | RoguesVRaiders | com.sipto.roguesvraiders | 1.7.0 | Sipto | ~4.1.0 |
| RUAFComeHomeServer/RUAFComeHomeServer.dll | RUAF Come Home | com.ruafcomehome.tacticaltoaster | 1.2.1 | TacticalToaster | ~4.1.5 |
| sgtlaggyQuestTweaks/sgtlaggyQuestTweaks.dll | sgtlaggy's Quest Tweaks | com.sgtlaggy.questtweaks | 4.9.0 | sgtlaggy | ~4.1.0 |
| ShinnisQuestsBackport/ShinnisQuestsBackport.dll | Shinni's 1.0 QuestsBackport | com.shinniuwu.questsbackport | 0.3.2 | ShinniUwU | ~4.1.0 |
| SitOnMyFace/SitOnMyFace.dll | SitOnMyFace | com.4nub15.sitonmyface | 0.1.2 | 4nub15 | ~4.1.6 |
| Solarint-SAIN-ServerMod/SAINServerMod.dll | SAIN | me.sol.sain | 4.5.1 | Solarint | ~4.1.3 |
| TerminalCommander/TerminalCommander.dll | TerminalCommander | com.mizmii.terminalcommander | 1.2.0 | mizmii | ~4.1.0 |
| Tyfon.HideoutInProgress.Server/Tyfon.HideoutInProgress.Server.dll | Hideout In Progress | com.tyfon.hideoutinprogress | 3.0.0 | Tyfon | ~4.1.0 |
| Tyfon.UIFixes.Server/Tyfon.UIFixes.Server.dll | UI Fixes | com.tyfon.uifixes | 6.0.4 | Tyfon | ~4.1.0 |
| untargh-server/untargh-server.dll | UNTAR Go Home! | com.untargh.tacticaltoaster | 3.2.1 | TacticalToaster | ~4.1.5 |
| VisitAPI-Server/VisitAPI-Server.dll | VisitAPI-Server | com.sora.visitapi.server | 1.3.7 | TricolourSky | ~4.1.0 |
| WolfiksHeavyTrooperMasks/WolfiksHeavyTroopers.dll | Wolfiks Heavy Troopers | com.hood.wolfiksheavytroopers | 1.2.0 | Hood | ~4.1.0 |
| WTT-Armory/WTT-Armory.dll | WTT-Armory | com.wtt.armory | 2.0.5 | GrooveypenguinX | ~4.1.0 |
| WTT-Artem/WTT-Artem.dll | Artem | com.crackbone.artem-wtt | 3.0.2 | Crackbone | ~4.1.0 |
| WTT-ContentBackport/WTT-ContentBackport.dll | WTT-ContentBackport | com.wtt.contentbackport | 2.0.7 | GrooveypenguinX | ~4.1.6 |
| WTT-ServerCommonLib/WTT-ServerCommonLib.dll | WTT-ServerCommonLib | com.wtt.commonlib | 3.0.6 | GrooveypenguinX | ~4.1.3 |
| [SVM] Server Value Modifier/ServerValueModifier.dll | SVM | fika.ghostfenixx.svm | 2.2.3 | GhostFenixx | ~4.1.3 |

## Headless 1 (taragarmr, `~/spt-headless/game/BepInEx`)
**Plugins**

| Folder/File | Name | GUID | Version |
|---|---|---|---|
| BlackDiv/BlackDiv.dll | BlackDiv | com.blackdiv.tacticaltoaster | 1.3.1 |
| CSGas/CSGasClient.dll | Manimal-CSGas | com.manimal.csgas | 2.0.0 |
| DrakiaXYZ-BigBrain.dll | DrakiaXYZ-BigBrain | xyz.drakia.bigbrain | 1.5.0 |
| DrakiaXYZ-GildedKeyStorage-Client.dll | DrakiaXYZ-GildedKeyStorage | xyz.drakia.gildedkeystorage | 2.1.1 |
| DrakiaXYZ-Waypoints/DrakiaXYZ-Waypoints.dll | DrakiaXYZ-Waypoints | xyz.drakia.waypoints | 1.9.0 |
| Fika/Fika.Core.dll | Fika.Core | com.fika.core | 2.4.6 |
| Fika/Fika.Headless.dll | Fika.Headless | com.fika.headless | 1.5.3 |
| ISBAishiPlugin/AishiKeysV2.dll | AishiMasterKeyFunction | com.samc137.aishi | 2.0.4 |
| ISBSpecialForcesPlugin/ISB-SpecialForces.dll | ISB Special Forces | com.samc137.isbspecialforces | 2.0.1 |
| ISBSpecialForcesPlugin/ISBNotify.dll | ISB SOF Notifier | com.samc137.ISBinfo | 2.0.1 |
| ISBSpecialForcesPlugin/ISBSOF_Extras.dll | ISB SOF: Immersion Systems Plugin | com.samc137.isbsofextras | 2.0.1 |
| KeepMeAlive/KeepMeAlive.dll | KeepMeAlive | com.keepmealive.fika | 1.1.0 |
| LennoxP90-FactoryClassic/FactoryClassic.Client.dll | FactoryClassic | com.lennoxp90.factoryclassic | 1.2.0 |
| LennoxP90-MapVariants/MapVariants.Client.dll | MapVariants | com.lennoxp90.mapvariants | 1.1.0 |
| Manimal-Halloween2026/Manimal.Halloween2026.Client.dll | Manimal-Halloween2026 | com.manimal.halloween2026 | 1.0.0 |
| Manimal-LabsBoiler/ManimalLabsBoiler.dll | Manimal-LabsBoiler | com.manimal.labsboiler | 2.0.0 |
| ManimalIcebreaker/ManimalIcebreakerClient.dll | Manimal-Icebreaker | com.manimal.icebreaker | 1.1.3 |
| ManimalIcebreaker/ManimalIcebreakerFika.dll | Manimal-IcebreakerFika | com.manimal.icebreaker.fika | 1.1.3 |
| ManimalInterchange/ManimalInterchange.dll | Manimal-InterchangeRework | com.manimal.interchange | 1.0.8 |
| ManimalLighthouse/ManimalLighthouse.dll | Manimal-LighthouseRework | com.manimal.lighthouse | 1.0.7 |
| ManimalTerminal/ManimalTerminalClient.dll | Manimal-Terminal | com.manimal.terminal | 0.2.20 |
| ManimalTerminal/ManimalTerminalFika.dll | Manimal-TerminalFika | com.manimal.terminal.fika | 0.2.20 |
| MoreBotsPlugin/MoreBotsPlugin.dll | MoreBotsAPI | com.morebotsapi.tacticaltoaster | 2.1.1 |
| ORBIT/ORBIT.dll | ORBIT | com.chazut.orbit | 2.1.1 |
| Orbit.Fika/Orbit.Fika.dll | ORBIT Fika Bridge | com.chazut.orbit.fika | 1.1.0 |
| PastPresentEventSelector/PastPresentEventSelector.Client.dll | zezinho-PastPresentEventSelector | com.zezinho.pastpresenteventselector | 1.7.7 |
| RUAFComeHomePlugin/RUAFComeHomePlugin.dll | RUAFComeHome | com.ruafcomehome.tacticaltoaster | 1.2.1 |
| RaiRai.ColorConverterAPI.dll | ColorConverterAPI | com.rairai.colorconverterapi.eft | 1.2.0 |
| RoguesVRaiders/RoguesVRaiders.dll | Rogues V Raiders | com.sipto.roguesvraiders | 1.7.0 |
| SAIN/SAIN.dll | SAIN | me.sol.sain | 4.5.1 |
| Tyfon.UIFixes.dll | Tyfon.UIFixes | com.tyfon.uifixes | 6.0.4 |
| UNTARGHPlugin/UNTARGHPlugin.dll | TacticalToaster-UNTARGH | com.untargh.tacticaltoaster | 3.2.1 |
| UnityToolkit/UnityToolkit.dll | Unity Toolkit | com.arys.unitytoolkit | 2.0.2 |
| UseItemsAnywhere/UseItemsAnywhere.dll | Use Items Anywhere | com.cj.useFromAnywhere | 2.1.6 |
| UseItemsAnywhere/UseItemsAnywhereFika.dll | Use Items Anywhere Fika | com.cj.UseItemsAnywhereFika | 1.0.0 |
| VisitAPI/VisitAPI.dll | VisitAPI | com.sora.visitapi | 1.3.7 |
| WTT-ArmoryClient/WTT-ArmoryClient.dll | WTT-Armory | com.wtt.armory | 2.0.5 |
| WTT-ClientCommonLib/WTT-ClientCommonLib.dll | WTT-ClientCommonLib | com.wtt.commonlib | 3.0.6 |
| WTT-ContentBackportClient/WTT-ContentBackportClient.dll | WTT-ContentBackportClient | com.wtt.contentbackport | 2.0.7 |
| acidphantasm-botplacementsystem/acidphantasm-botplacementsystem.dll | acidphantasm-botplacementsystem | com.acidphantasm.botplacementsystem | 2.1.0 |
| archangelwtf-contentbackportprestiges/ContentBackportPrestigesClient.dll | Content Backport - Prestiges | wtf.archangel.contentbackportprestiges | 1.1.0 |
| krane.InfectedVariants.dll | Krane's InfectedVariants | com.krane.infectedvariants | 1.3.0 |
| ozen.MagCheckInterrupt/ozen.MagCheckInterrupt.dll | Magazine Check Interrupt | com.ozen.magcheckinterrupt | 1.1.1 |
| spt/ConfigurationManager/ConfigurationManager.dll | Configuration Manager | com.bepis.bepinex.configurationmanager | 18.4 |
| spt/spt-core.dll | SPT.Core | com.SPT.core | 4.1.6 |
| spt/spt-custom.dll | SPT.Custom | com.SPT.custom | 4.1.6 |
| spt/spt-debugging.dll | SPT.Debugging | com.SPT.debugging | 4.1.6 |
| spt/spt-singleplayer.dll | SPT.Singleplayer | com.SPT.singleplayer | 4.1.6 |
| tarkin-ladders/tarkin.ladders.bep.dll | tarkin.ladders.bep | com.tarkin.ladders | 1.0.4 |
| tarkin-ladders/tarkin.ladders.fika.dll | tarkin.ladders.fika | com.tarkin.ladders.fika | 1.0.1 |
| _28 other dll(s) without BepInPlugin (libraries/native)_ | | | |

**Patchers**

| Folder/File | Name | GUID | Version |
|---|---|---|---|
| BlackDiv/BlackDiv.dll | BlackDiv Prepatch | com.blackdiv.tacticaltoaster | 1.3.1 |
| FixPluginTypesSerialization.dll | FixPluginTypesSerialization | - | 1.0.0+784e45ae02f1c3509d261f0e9e1695b396ba6f8c (asm 1.0.0.0) |
| ISBSpecialForces.dll | ISBSpecialForces | com.samc137.isbspecialforces | 2.0.1 |
| ManimalTerminalPrepatch/ManimalTerminalPrepatch.dll | ManimalTerminalPrepatch | - | 0.2.20+1d45c1c92910a202d2bfd34e671e24c67df1d833 (asm 0.2.20.0) |
| MoreBotsPrepatch/MoreBotsPrepatch.dll | MoreBotsPrepatch | com.morebotsapiprepatch.tacticaltoaster | 2.1.1 |
| PastPresentEventSelector/PastPresentEventSelector.Prepatch.dll | PastPresentEventSelector.Prepatch | - | 1.7.7+fbd32b38b7b24dd1e172ce7dc820a48324859066 (asm 1.7.7.0) |
| RUAFComeHome/RUAFComeHome.dll | RUAFComeHome Prepatch | com.ruafcomehome.tacticaltoaster | 1.2.1 |
| UNTARGHPrepatch/UNTARGHPrepatch.dll | TacticalToaster-UNTARGHPrePatch | com.untarghprepatch.tacticaltoaster | 3.2.1 |
| UnityToolkit/System.Runtime.CompilerServices.Unsafe.dll | System.Runtime.CompilerServices.Unsafe | - | 6.1.2 (asm 6.0.0.0) |
| UnityToolkit/UnityToolkit-Prepatcher.dll | UnityToolkit-Prepatcher | - | 2.0.2 (asm 2.0.2.0) |
| WTT-ContentBackportPatcher/WTT-ContentBackportPatcher.dll | Content Backport Preloader Patch | com.wtt.contentbackport | 2.0.7 |
| spt-prepatch.dll | spt-prepatch | - | 4.1.6+d52d9c99836b6d7dc5ad93852cd8032158df0f9c (asm 4.1.6.0) |

## Headless 2 (taragarmr, `~/spt-headless-2/game/BepInEx`)
**Plugins**

| Folder/File | Name | GUID | Version |
|---|---|---|---|
| BlackDiv/BlackDiv.dll | BlackDiv | com.blackdiv.tacticaltoaster | 1.3.1 |
| CSGas/CSGasClient.dll | Manimal-CSGas | com.manimal.csgas | 2.0.0 |
| DrakiaXYZ-BigBrain.dll | DrakiaXYZ-BigBrain | xyz.drakia.bigbrain | 1.5.0 |
| DrakiaXYZ-GildedKeyStorage-Client.dll | DrakiaXYZ-GildedKeyStorage | xyz.drakia.gildedkeystorage | 2.1.1 |
| DrakiaXYZ-Waypoints/DrakiaXYZ-Waypoints.dll | DrakiaXYZ-Waypoints | xyz.drakia.waypoints | 1.9.0 |
| Fika/Fika.Core.dll | Fika.Core | com.fika.core | 2.4.6 |
| Fika/Fika.Headless.dll | Fika.Headless | com.fika.headless | 1.5.3 |
| ISBAishiPlugin/AishiKeysV2.dll | AishiMasterKeyFunction | com.samc137.aishi | 2.0.4 |
| ISBSpecialForcesPlugin/ISB-SpecialForces.dll | ISB Special Forces | com.samc137.isbspecialforces | 2.0.1 |
| ISBSpecialForcesPlugin/ISBNotify.dll | ISB SOF Notifier | com.samc137.ISBinfo | 2.0.1 |
| ISBSpecialForcesPlugin/ISBSOF_Extras.dll | ISB SOF: Immersion Systems Plugin | com.samc137.isbsofextras | 2.0.1 |
| KeepMeAlive/KeepMeAlive.dll | KeepMeAlive | com.keepmealive.fika | 1.1.0 |
| LennoxP90-FactoryClassic/FactoryClassic.Client.dll | FactoryClassic | com.lennoxp90.factoryclassic | 1.2.0 |
| LennoxP90-MapVariants/MapVariants.Client.dll | MapVariants | com.lennoxp90.mapvariants | 1.1.0 |
| Manimal-Halloween2026/Manimal.Halloween2026.Client.dll | Manimal-Halloween2026 | com.manimal.halloween2026 | 1.0.0 |
| Manimal-LabsBoiler/ManimalLabsBoiler.dll | Manimal-LabsBoiler | com.manimal.labsboiler | 2.0.0 |
| ManimalIcebreaker/ManimalIcebreakerClient.dll | Manimal-Icebreaker | com.manimal.icebreaker | 1.1.3 |
| ManimalIcebreaker/ManimalIcebreakerFika.dll | Manimal-IcebreakerFika | com.manimal.icebreaker.fika | 1.1.3 |
| ManimalInterchange/ManimalInterchange.dll | Manimal-InterchangeRework | com.manimal.interchange | 1.0.8 |
| ManimalLighthouse/ManimalLighthouse.dll | Manimal-LighthouseRework | com.manimal.lighthouse | 1.0.7 |
| ManimalTerminal/ManimalTerminalClient.dll | Manimal-Terminal | com.manimal.terminal | 0.2.20 |
| ManimalTerminal/ManimalTerminalFika.dll | Manimal-TerminalFika | com.manimal.terminal.fika | 0.2.20 |
| MoreBotsPlugin/MoreBotsPlugin.dll | MoreBotsAPI | com.morebotsapi.tacticaltoaster | 2.1.1 |
| ORBIT/ORBIT.dll | ORBIT | com.chazut.orbit | 2.1.1 |
| Orbit.Fika/Orbit.Fika.dll | ORBIT Fika Bridge | com.chazut.orbit.fika | 1.1.0 |
| PastPresentEventSelector/PastPresentEventSelector.Client.dll | zezinho-PastPresentEventSelector | com.zezinho.pastpresenteventselector | 1.7.7 |
| RUAFComeHomePlugin/RUAFComeHomePlugin.dll | RUAFComeHome | com.ruafcomehome.tacticaltoaster | 1.2.1 |
| RaiRai.ColorConverterAPI.dll | ColorConverterAPI | com.rairai.colorconverterapi.eft | 1.2.0 |
| RoguesVRaiders/RoguesVRaiders.dll | Rogues V Raiders | com.sipto.roguesvraiders | 1.7.0 |
| SAIN/SAIN.dll | SAIN | me.sol.sain | 4.5.1 |
| Tyfon.UIFixes.dll | Tyfon.UIFixes | com.tyfon.uifixes | 6.0.4 |
| UNTARGHPlugin/UNTARGHPlugin.dll | TacticalToaster-UNTARGH | com.untargh.tacticaltoaster | 3.2.1 |
| UnityToolkit/UnityToolkit.dll | Unity Toolkit | com.arys.unitytoolkit | 2.0.2 |
| UseItemsAnywhere/UseItemsAnywhere.dll | Use Items Anywhere | com.cj.useFromAnywhere | 2.1.6 |
| UseItemsAnywhere/UseItemsAnywhereFika.dll | Use Items Anywhere Fika | com.cj.UseItemsAnywhereFika | 1.0.0 |
| VisitAPI/VisitAPI.dll | VisitAPI | com.sora.visitapi | 1.3.7 |
| WTT-ArmoryClient/WTT-ArmoryClient.dll | WTT-Armory | com.wtt.armory | 2.0.5 |
| WTT-ClientCommonLib/WTT-ClientCommonLib.dll | WTT-ClientCommonLib | com.wtt.commonlib | 3.0.6 |
| WTT-ContentBackportClient/WTT-ContentBackportClient.dll | WTT-ContentBackportClient | com.wtt.contentbackport | 2.0.7 |
| acidphantasm-botplacementsystem/acidphantasm-botplacementsystem.dll | acidphantasm-botplacementsystem | com.acidphantasm.botplacementsystem | 2.1.0 |
| archangelwtf-contentbackportprestiges/ContentBackportPrestigesClient.dll | Content Backport - Prestiges | wtf.archangel.contentbackportprestiges | 1.1.0 |
| krane.InfectedVariants.dll | Krane's InfectedVariants | com.krane.infectedvariants | 1.3.0 |
| ozen.MagCheckInterrupt/ozen.MagCheckInterrupt.dll | Magazine Check Interrupt | com.ozen.magcheckinterrupt | 1.1.1 |
| spt/ConfigurationManager/ConfigurationManager.dll | Configuration Manager | com.bepis.bepinex.configurationmanager | 18.4 |
| spt/spt-core.dll | SPT.Core | com.SPT.core | 4.1.6 |
| spt/spt-custom.dll | SPT.Custom | com.SPT.custom | 4.1.6 |
| spt/spt-debugging.dll | SPT.Debugging | com.SPT.debugging | 4.1.6 |
| spt/spt-singleplayer.dll | SPT.Singleplayer | com.SPT.singleplayer | 4.1.6 |
| tarkin-ladders/tarkin.ladders.bep.dll | tarkin.ladders.bep | com.tarkin.ladders | 1.0.4 |
| tarkin-ladders/tarkin.ladders.fika.dll | tarkin.ladders.fika | com.tarkin.ladders.fika | 1.0.1 |
| _28 other dll(s) without BepInPlugin (libraries/native)_ | | | |

**Patchers**

| Folder/File | Name | GUID | Version |
|---|---|---|---|
| BlackDiv/BlackDiv.dll | BlackDiv Prepatch | com.blackdiv.tacticaltoaster | 1.3.1 |
| FixPluginTypesSerialization.dll | FixPluginTypesSerialization | - | 1.0.0+784e45ae02f1c3509d261f0e9e1695b396ba6f8c (asm 1.0.0.0) |
| ISBSpecialForces.dll | ISBSpecialForces | com.samc137.isbspecialforces | 2.0.1 |
| ManimalTerminalPrepatch/ManimalTerminalPrepatch.dll | ManimalTerminalPrepatch | - | 0.2.20+1d45c1c92910a202d2bfd34e671e24c67df1d833 (asm 0.2.20.0) |
| MoreBotsPrepatch/MoreBotsPrepatch.dll | MoreBotsPrepatch | com.morebotsapiprepatch.tacticaltoaster | 2.1.1 |
| PastPresentEventSelector/PastPresentEventSelector.Prepatch.dll | PastPresentEventSelector.Prepatch | - | 1.7.7+fbd32b38b7b24dd1e172ce7dc820a48324859066 (asm 1.7.7.0) |
| RUAFComeHome/RUAFComeHome.dll | RUAFComeHome Prepatch | com.ruafcomehome.tacticaltoaster | 1.2.1 |
| UNTARGHPrepatch/UNTARGHPrepatch.dll | TacticalToaster-UNTARGHPrePatch | com.untarghprepatch.tacticaltoaster | 3.2.1 |
| UnityToolkit/System.Runtime.CompilerServices.Unsafe.dll | System.Runtime.CompilerServices.Unsafe | - | 6.1.2 (asm 6.0.0.0) |
| UnityToolkit/UnityToolkit-Prepatcher.dll | UnityToolkit-Prepatcher | - | 2.0.2 (asm 2.0.2.0) |
| WTT-ContentBackportPatcher/WTT-ContentBackportPatcher.dll | Content Backport Preloader Patch | com.wtt.contentbackport | 2.0.7 |
| spt-prepatch.dll | spt-prepatch | - | 4.1.6+d52d9c99836b6d7dc5ad93852cd8032158df0f9c (asm 4.1.6.0) |

## Client tarmantria (`~/Games/spt/drive_c/SPT/BepInEx`)
**Plugins**

| Folder/File | Name | GUID | Version |
|---|---|---|---|
| 7Bpencil.BrighterInteriors.dll | 7Bpencil.BrighterInteriors | 7Bpencil.BrighterInteriors | 1.1.1 |
| AllQuestsCheckmarks/AllQuestsCheckmarks-Client.dll | ZGFueDkx-AllQuestsCheckmarks | com.zgfuedkx.allquestscheckmarks | 1.4.0 |
| AutoKeycard/AutoKeycard.dll | Auto Keycard | com.cyanfridge.autokeycard | 1.0.1 |
| BetterBepInEx.dll | BetterBepInEx | com.mybutthasarash.betterbepinex | 1.0.2 |
| BlackDiv/BlackDiv.dll | BlackDiv | com.blackdiv.tacticaltoaster | 1.3.1 |
| CSGas/CSGasClient.dll | Manimal-CSGas | com.manimal.csgas | 2.0.0 |
| ContinuousHealing/ContinuousHealing.dll | ContinuousHealing | com.lacyway.ch | 1.6.3 |
| DisableAlphaLabel.dll | AcksBerg-DisableVersionLabelInRaid | com.acksberg.disableversionlabelinraid | 1.0.0 |
| DrakiaXYZ-BigBrain.dll | DrakiaXYZ-BigBrain | xyz.drakia.bigbrain | 1.5.0 |
| DrakiaXYZ-GildedKeyStorage-Client.dll | DrakiaXYZ-GildedKeyStorage | xyz.drakia.gildedkeystorage | 2.1.1 |
| DrakiaXYZ-QuestTracker/DrakiaXYZ-QuestTracker.dll | DrakiaXYZ-QuestTracker | xyz.drakia.questtracker | 1.7.0 |
| DrakiaXYZ-TaskListFixes.dll | DrakiaXYZ-TaskListFixes | xyz.drakia.tasklistfixes | 1.8.0 |
| DrakiaXYZ-Waypoints/DrakiaXYZ-Waypoints.dll | DrakiaXYZ-Waypoints | xyz.drakia.waypoints | 1.9.0 |
| FastGrenadeThrow/FastGrenadeThrow.dll | Fast Grenade Throw | com.vultify.fastgrenadethrow | 2.0.0 |
| Fika/Fika.Core.dll | Fika.Core | com.fika.core | 2.4.6 |
| HandsAreNotBusy.dll | HandsAreNotBusy | com.lacyway.hanb | 1.7.1 |
| ISBAishiPlugin/AishiKeysV2.dll | AishiMasterKeyFunction | com.samc137.aishi | 2.0.4 |
| ISBSpecialForcesPlugin/ISB-SpecialForces.dll | ISB Special Forces | com.samc137.isbspecialforces | 2.0.1 |
| ISBSpecialForcesPlugin/ISBNotify.dll | ISB SOF Notifier | com.samc137.ISBinfo | 2.0.1 |
| ISBSpecialForcesPlugin/ISBSOF_Extras.dll | ISB SOF: Immersion Systems Plugin | com.samc137.isbsofextras | 2.0.1 |
| Kaeno-TraderScrolling.dll | Kaeno-TraderScrolling | com.kaeno.TraderScrolling | 4.0.0 |
| Kat.BetterAmmoLoadingList.dll | BALL - BetterAmmoLoadingList | katrin0522.betterammoloadinglist | 1.1.1 |
| KeepMeAlive/KeepMeAlive.dll | KeepMeAlive | com.keepmealive.fika | 1.1.0 |
| Lawnmower.dll | Lawnmower | hazelify.Lawnmower | 1.1.0 |
| LennoxP90-FactoryClassic/FactoryClassic.Client.dll | FactoryClassic | com.lennoxp90.factoryclassic | 1.2.0 |
| LennoxP90-MapVariants/MapVariants.Client.dll | MapVariants | com.lennoxp90.mapvariants | 1.1.0 |
| Manimal-Halloween2026/Manimal.Halloween2026.Client.dll | Manimal-Halloween2026 | com.manimal.halloween2026 | 1.0.0 |
| Manimal-LabsBoiler/ManimalLabsBoiler.dll | Manimal-LabsBoiler | com.manimal.labsboiler | 2.0.0 |
| ManimalIcebreaker/ManimalIcebreakerClient.dll | Manimal-Icebreaker | com.manimal.icebreaker | 1.1.3 |
| ManimalIcebreaker/ManimalIcebreakerFika.dll | Manimal-IcebreakerFika | com.manimal.icebreaker.fika | 1.1.3 |
| ManimalInterchange/ManimalInterchange.dll | Manimal-InterchangeRework | com.manimal.interchange | 1.0.8 |
| ManimalLighthouse/ManimalLighthouse.dll | Manimal-LighthouseRework | com.manimal.lighthouse | 1.0.7 |
| MoreBotsPlugin/MoreBotsPlugin.dll | MoreBotsAPI | com.morebotsapi.tacticaltoaster | 2.1.1 |
| ORBIT/ORBIT.dll | ORBIT | com.chazut.orbit | 2.1.1 |
| Orbit.Fika/Orbit.Fika.dll | ORBIT Fika Bridge | com.chazut.orbit.fika | 1.1.0 |
| PastPresentEventSelector/PastPresentEventSelector.Client.dll | zezinho-PastPresentEventSelector | com.zezinho.pastpresenteventselector | 1.7.7 |
| PlayerEncumbranceBar/PlayerEncumbranceBar.dll | PlayerEncumbranceBar | com.mpstark.PlayerEncumbranceBar | 1.3.0 |
| QuickMeds/QuickMeds.dll | BlackHawk-QuickMeds | com.blackhawk.quickmeds | 1.2.0 |
| QuickSellFlea/QuickSellFlea.dll | QuickSellFlea | com.lacyway.csf | 1.4.5 |
| QuietWeather.dll | QuietWeather | com.wade.quietweather | 1.0.1 |
| RaiRai.ColorConverterAPI.dll | ColorConverterAPI | com.rairai.colorconverterapi.eft | 1.2.0 |
| RoguesVRaiders/RoguesVRaiders.dll | Rogues V Raiders | com.sipto.roguesvraiders | 1.7.0 |
| SAIN/SAIN.dll | SAIN | me.sol.sain | 4.5.1 |
| SmajlecStamina.dll | SmajlecStamina | com.smajlec.stamina | 1.4.0 |
| Terkoiz.FlareEventNotifier/Terkoiz-FlareEventNotifier.dll | Terkoiz.FlareEventNotifier | com.terkoiz.flareeventnotifier | 1.0.6 |
| Terkoiz.Skipper/terkoiz-skipper.dll | Terkoiz.Skipper | com.terkoiz.skipper | 1.1.5 |
| Tyfon.AutoDeposit.dll | Tyfon.AutoDeposit | Tyfon.AutoDeposit | 6.0.0 |
| Tyfon.HideoutInProgress.dll | Tyfon.HideoutInProgress | com.tyfon.hideoutinprogress | 3.0.0 |
| Tyfon.UIFixes.dll | Tyfon.UIFixes | com.tyfon.uifixes | 6.0.4 |
| UNTARGHPlugin/UNTARGHPlugin.dll | TacticalToaster-UNTARGH | com.untargh.tacticaltoaster | 3.2.1 |
| UnityToolkit/UnityToolkit.dll | Unity Toolkit | com.arys.unitytoolkit | 2.0.2 |
| UseItemsAnywhere/UseItemsAnywhere.dll | Use Items Anywhere | com.cj.useFromAnywhere | 2.1.6 |
| UseItemsAnywhere/UseItemsAnywhereFika.dll | Use Items Anywhere Fika | com.cj.UseItemsAnywhereFika | 1.0.0 |
| VisitAPI/VisitAPI.dll | VisitAPI | com.sora.visitapi | 1.3.7 |
| WTT-ArmoryClient/WTT-ArmoryClient.dll | WTT-Armory | com.wtt.armory | 2.0.5 |
| WTT-ClientCommonLib/WTT-ClientCommonLib.dll | WTT-ClientCommonLib | com.wtt.commonlib | 3.0.6 |
| WTT-ContentBackportClient/WTT-ContentBackportClient.dll | WTT-ContentBackportClient | com.wtt.contentbackport | 2.0.7 |
| acidphantasm-botplacementsystem/acidphantasm-botplacementsystem.dll | acidphantasm-botplacementsystem | com.acidphantasm.botplacementsystem | 2.1.0 |
| archangelwtf-contentbackportprestiges/ContentBackportPrestigesClient.dll | Content Backport - Prestiges | wtf.archangel.contentbackportprestiges | 1.1.0 |
| krane.AdsCameraDistance.dll (symlink @ 2000e48) | Krane's AdsCameraDistance | krane.adscameradistance | 1.0.0 |
| krane.BtrInteriorVolume.dll (symlink @ 30dd1eb) | Krane's BtrInteriorVolume | krane.btrinteriorvolume | 1.0.0 |
| krane.DecalLockFix.dll (symlink @ 2b0f28c) | DecalLockFix | krane.decallockfix | 1.0.0 |
| krane.FleaOfferGuard.dll (symlink @ cb87b6a) | Krane's FleaOfferGuard | krane.fleaofferguard | 1.0.0 |
| krane.HeadlessGuard.dll (symlink @ a885272) | Krane's HeadlessGuard | krane.headlessguard | 1.0.0 |
| krane.InfectedVariants.dll (symlink @ e5637cf) | Krane's InfectedVariants | com.krane.infectedvariants | 1.3.0 |
| krane.InstantTraderDialogs.dll (symlink @ c524d12) | Krane's InstantTraderDialogs | krane.instanttraderdialogs | 1.0.0 |
| krane.ItemTweaks.dll (symlink @ 77d43c8) | Krane's Item Tweaks & Additions | krane.itemtweaks | 1.0.0 |
| krane.JoinInsurance.dll (symlink @ 2b0f28c) | Krane's JoinInsurance | krane.joininsurance | 1.0.0 |
| krane.LoadSpeedFix.dll (symlink @ 2b0f28c) | Krane's LoadSpeedFix | krane.loadspeedfix | 1.0.0 |
| krane.MagTopUp.dll (symlink @ b6f8784) | Krane's MagTopUp | krane.magtopup | 1.1.0 |
| krane.PrestigeTransferFilledContainers.dll (symlink @ 67a3e11) | PrestigeTransferFilledContainers | krane.prestigetransferfilledcontainers | 1.0.0 |
| krane.RememberInsurer.dll (symlink @ 7ddbd79) | Krane's RememberInsurer | krane.rememberinsurer | 1.0.0 |
| krane.ScopeZoomPresets.dll (symlink @ 9b1fce8) | Krane's ScopeZoomPresets | krane.scopezoompresets | 1.0.0 |
| krane.SearchSpeed.dll (symlink @ 0858d00) | Krane's SearchSpeed | krane.searchspeed | 1.0.0 |
| krane.TraderTweaks.dll (symlink @ a8e18ad) | Krane's TraderTweaks | krane.tradertweaks | 1.0.0 |
| krane.UseItemsAnywhereTweaks.dll (symlink @ 0486e42) | Krane's Use Items Anywhere Tweaks | krane.UseItemsAnywhereTweaks | 5.1.0 |
| krane.VisitButtonFix.dll (symlink @ ab58b37) | VisitButtonFix | krane.visitbuttonfix | 1.0.0 |
| maschine-TraderSearch.dll | maschine-TraderSearch | com.maschine.TraderSearch | 2.1.0 |
| mpstark-dynamicmaps/DynamicMaps.dll | DynamicMaps | com.mpstark.dynamicmaps | 1.2.1 |
| ozen.ContinuousLoadAmmo/ozen.ContinuousLoadAmmo.dll | Continuous Load Ammo | com.ozen.continuousloadammo | 1.2.0 |
| spt/ConfigurationManager/ConfigurationManager.dll | Configuration Manager | com.bepis.bepinex.configurationmanager | 18.4 |
| spt/spt-core.dll | SPT.Core | com.SPT.core | 4.1.6 |
| spt/spt-custom.dll | SPT.Custom | com.SPT.custom | 4.1.6 |
| spt/spt-debugging.dll | SPT.Debugging | com.SPT.debugging | 4.1.6 |
| spt/spt-singleplayer.dll | SPT.Singleplayer | com.SPT.singleplayer | 4.1.6 |
| tarkin-ladders/tarkin.ladders.bep.dll | tarkin.ladders.bep | com.tarkin.ladders | 1.0.4 |
| tarkin-ladders/tarkin.ladders.fika.dll | tarkin.ladders.fika | com.tarkin.ladders.fika | 1.0.1 |
| _27 other dll(s) without BepInPlugin (libraries/native)_ | | | |

**Patchers**

| Folder/File | Name | GUID | Version |
|---|---|---|---|
| BlackDiv/BlackDiv.dll | BlackDiv Prepatch | com.blackdiv.tacticaltoaster | 1.3.1 |
| FixPluginTypesSerialization.dll | FixPluginTypesSerialization | - | 1.0.0+784e45ae02f1c3509d261f0e9e1695b396ba6f8c (asm 1.0.0.0) |
| ISBSpecialForces.dll | ISBSpecialForces | com.samc137.isbspecialforces | 2.0.1 |
| MoreBotsPrepatch/MoreBotsPrepatch.dll | MoreBotsPrepatch | com.morebotsapiprepatch.tacticaltoaster | 2.1.1 |
| PastPresentEventSelector/PastPresentEventSelector.Prepatch.dll | PastPresentEventSelector.Prepatch | - | 1.7.7+fbd32b38b7b24dd1e172ce7dc820a48324859066 (asm 1.7.7.0) |
| UNTARGHPrepatch/UNTARGHPrepatch.dll | TacticalToaster-UNTARGHPrePatch | com.untarghprepatch.tacticaltoaster | 3.2.1 |
| UnityToolkit/System.Runtime.CompilerServices.Unsafe.dll | System.Runtime.CompilerServices.Unsafe | - | 6.1.2 (asm 6.0.0.0) |
| UnityToolkit/UnityToolkit-Prepatcher.dll | UnityToolkit-Prepatcher | - | 2.0.2 (asm 2.0.2.0) |
| WTT-ContentBackportPatcher/WTT-ContentBackportPatcher.dll | Content Backport Preloader Patch | com.wtt.contentbackport | 2.0.7 |
| spt-prepatch.dll | spt-prepatch | - | 4.1.6+d52d9c99836b6d7dc5ad93852cd8032158df0f9c (asm 4.1.6.0) |

## Client tariognatha

Not reachable from tarmantria. On tariognatha: `git pull` this repo, run `scripts/inventory.sh local` (needs `dotnet` 10 or nix; reads `~/Games/spt/drive_c/SPT/BepInEx`), and paste the output here as `**Plugins**` / `**Patchers**` tables.

_(placeholder)_

## Differences

**tarmantria vs headless**: no version mismatches among the plugins both have. Headless 1 and 2 are identical (plugins and patchers, same versions).

Headless only (client lacks them):

| Item | Version | Note |
|---|---|---|
| Fika.Headless | 1.5.3 | expected |
| ManimalTerminal plugins (`ManimalTerminalClient`, `ManimalTerminalFika`) + `ManimalTerminalPrepatch` | 0.2.20 | server has ManimalTerminal 0.2.20; client has no counterpart, check |
| RUAFComeHomePlugin + `RUAFComeHome` patcher | 1.2.1 | server has RUAFComeHomeServer 1.2.1; client has no counterpart, check |
| ozen.MagCheckInterrupt | 1.1.1 | headless only |

Client only (expected UI/QoL, not needed on headless): BrighterInteriors, Tyfon.AutoDeposit, DisableAlphaLabel, QuickMeds, AutoKeycard, TraderScrolling, ContinuousHealing, QuickSellFlea, HandsAreNotBusy, TraderSearch, PlayerEncumbranceBar, DynamicMaps, BetterBepInEx, ContinuousLoadAmmo, SmajlecStamina, FlareEventNotifier, Skipper, FastGrenadeThrow, QuietWeather, Lawnmower, BetterAmmoLoadingList, DrakiaXYZ QuestTracker and TaskListFixes, and all `krane.*` plugins (none are on headless; includes `krane.HeadlessGuard`). Gameplay-relevant client-only: Tyfon.HideoutInProgress 3.0.0, AllQuestsCheckmarks-Client 1.4.0 (both have server counterparts).

**tarmantria vs tariognatha**: _(placeholder, fill after pasting the tariognatha section; diff the Plugins/Patchers tables by GUID and Version)_

## Pending archives and backups

| Where | Path |
|---|---|
| Install archives, tarmantria | `~/Games/spt/pending-server-mods` |
| Install archives, taragarmr | `~/spt-pending-mods` |
| Backups, taragarmr | `~/spt-mod-backups/2026-11-15` |
| Backups, tarmantria | `~/Games/spt/backups` |
