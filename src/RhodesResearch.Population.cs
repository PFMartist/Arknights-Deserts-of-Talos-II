using System;
using System.Collections.Generic;
using BBI.Core;
using BBI.Core.Data;
using BBI.Core.Utility.FixedPoint;
using BBI.Game.Data;
using UnityEngine;

namespace EndfieldDoK.MapUnitLab
{
    public static partial class RhodesResearch
    {
        public const int SkirmishStartingPopulation = 25;
        public const int SkirmishMaximumPopulation = 125;
        public const string Population1 = "Research_RI_FleetCapacity_1";
        public const string Population2 = "Research_RI_FleetCapacity_2";
        public const string Population3 = "Research_RI_FleetCapacity_3";
        public const string Population4 = "Research_RI_FleetCapacity_4";
        public const string Population5 = "Research_RI_FleetCapacity_5";
        const int PopulationAttributeID = 524288; // Buff.CategoryAndID.Commander_PopulationCap

        static string[] PopulationResearchKeys()
        { return new string[] { Population1, Population2, Population3, Population4, Population5 }; }

        static string[] BuildResearchKeys()
        {
            List<string> result = new List<string>(new string[] { LightAssembly, SuppressionAssembly, RailgunAssembly,
                AirDefenceAssembly, TiltAssembly, ArtilleryAssembly, Aviation1, Gunnery1, Gunnery2, Gunnery3, AdvancedOriginiumSuppressant, EscortBoost });
            // The old laboratory deliberately uses100/100. Do not add five
            // ineffective upgrades to that separate test profile.
            if (Runtime.SkirmishMode){result.AddRange(PopulationResearchKeys());result.AddRange(ReactorResearchKeys());}
            return result.ToArray();
        }

        // Called only on the privately cloned menu template, before appending it
        // to the LevelManager. No source commander, field-survey scanner or
        // native faction is modified; no simulation tick writes any population.
        public static void ConfigureSkirmishPopulationOpening(CommanderAttributes privateCopy)
        {
            if (!Runtime.SkirmishMode) return;
            if (privateCopy == null) throw new ArgumentNullException("privateCopy");
            StartingUnit[] source = privateCopy.StartingUnits;
            if (source == null) throw new InvalidOperationException("Rhodes private starting fleet is missing");
            // StartingUnit is a class. Clone even retained entries, so the
            // caller cannot accidentally leave native spawn-tag objects shared.
            StartingUnit[] isolated = (StartingUnit[])Copy(source, new Dictionary<object, object>(new Identity()));
            List<StartingUnit> retained = new List<StartingUnit>();
            int removed = 0;
            foreach (StartingUnit unit in isolated)
            {
                if (unit != null && (unit.Unit == "C_PopcapScanner" || unit.Unit == "C_Sob_PopcapScanner")) { removed++; continue; }
                retained.Add(unit);
            }
            privateCopy.StartingUnits = retained.ToArray();
            foreach (string field in new string[] { "m_StartingPopCap", "m_StartingPopCapEasyDifficulty", "m_StartingPopCapHardDifficulty" })
                Set(privateCopy, field, SkirmishStartingPopulation);
            foreach (string field in new string[] { "m_MaximumPopCap", "m_MaximumPopCapEasyDifficulty", "m_MaximumPopCapHardDifficulty" })
                Set(privateCopy, field, SkirmishMaximumPopulation);
            VerifySkirmishPopulationOpening(privateCopy);
            Runtime.Log("RHODES_SKIRMISH_POPULATION_OPENING initial=25; maximum=125; legacyPopulationScannersRemoved=" + removed +
                "; ordinarySurveyScannerUnchanged=True; sourceStartingEntriesCloned=True; runtimePopulationWrites=False");
        }

        static void VerifySkirmishPopulationOpening(CommanderAttributes commander)
        {
            if (!Runtime.SkirmishMode) return;
            if (commander == null || commander.StartingPopCap != SkirmishStartingPopulation || commander.MaximumPopCap != SkirmishMaximumPopulation)
                throw new InvalidOperationException("Private Rhodes skirmish requires starting population25 and maximum125");
            foreach (StartingUnit unit in commander.StartingUnits)
                if (unit != null && (unit.Unit == "C_PopcapScanner" || unit.Unit == "C_Sob_PopcapScanner"))
                    throw new InvalidOperationException("Legacy population scanner would add another15 to the private Rhodes opening");
        }

        static void RegisterPopulationResearch()
        {
            if (!Runtime.SkirmishMode) return;
            string[] keys = PopulationResearchKeys();
            int[] expectedCosts = { 250, 310, 390, 490, 610 };
            string[] numerals = { "I", "II", "III", "IV", "V" };
            for (int index = 0; index < keys.Length; index++)
            {
                string nativeKey = "Research_Buff_Fleet_Popcap_" + (index + 1);
                EntityTypeAttributes definition = Registry.GetEntityType(nativeKey);
                ResearchItemAttributes donor = definition == null ? null : definition.Get<ResearchItemAttributes>();
                if (donor == null) throw new InvalidOperationException("Native population research baseline missing: " + nativeKey);
                if (Convert.ToInt32(NativeRead.Get(donor, "m_Resource1Cost")) != expectedCosts[index] ||
                    Convert.ToInt32(NativeRead.Get(donor, "m_Resource2Cost")) != 0 ||
                    donor.ResearchTime != Fixed64.FromInt(10))
                    throw new InvalidOperationException("Native population research baseline changed: " + nativeKey);
                Array donorTargets = NativeRead.Get(donor, "m_UnitTypeBuffs") as Array;
                if (donorTargets == null || donorTargets.Length != 1) throw new InvalidOperationException("Unexpected native population buff target count");
                object donorTarget = donorTargets.GetValue(0);
                if ((string)NativeRead.Get(donorTarget, "m_UnitTypePrefix") != "G_Carrier_MP")
                    throw new InvalidOperationException("Unexpected native population research donor target");
                ScriptableObject donorBuff = NativeRead.Get(donorTarget, "m_BuffSet") as ScriptableObject;
                if (donorBuff == null) throw new InvalidOperationException("Native population buff asset is absent");
                object donorBuffData = NativeRead.Get(donorBuff, "m_Attributes");
                VerifyPopulationBuff(donorBuffData);

                ScriptableObject ownBuff = Create(NativeType("BuffSetAttributesAsset"), keys[index] + "_CommanderPopulation_Buff");
                object ownBuffData = Copy(donorBuffData, new Dictionary<object, object>(new Identity()));
                Set(ownBuff, "m_Attributes", ownBuffData); Enable(ownBuff);

                object target = Copy(donorTarget, new Dictionary<object, object>(new Identity()));
                Set(target, "m_UnitTypePrefix", RhodesCarrierUnits.UnitKey); Set(target, "m_UseAsPrefix", false); Set(target, "m_BuffSet", ownBuff);
                object data = Copy(donor, new Dictionary<object, object>(new Identity()));
                ((NamedObjectBase)data).InitializeName(keys[index]);
                Set(data, "m_ResearchType", ResearchType.Engineering);
                ApplyResearchIcon(data,keys[index]);
                ApplyResearchBalance(data,keys[index]);
                Set(data, "m_ResearchDependencies", index == 0 ? new string[0] : new string[] { keys[index - 1] });
                Set(data, "m_UnitTypeBuffs", ArrayOf(target));
                string title = "舰队容量 " + numerals[index];
                Set(data, "m_LocalizedResearchTitleStringID", title);
                Set(data, "m_LocalizedShortDescriptionStringID", "舰队容量 +20");
                Set(data, "m_LocalizedLongDescriptionStringID", "舰队容量增加20点，提升至" + (SkirmishStartingPopulation + 20 * (index + 1)) +
                    "点。完成全部扩容后，容量上限为125点。" );
                // Preserve the native population voice. The formal catalog
                // deliberately keeps its native costs and ten-second clock.
                ScriptableObject research = Create(NativeType("ResearchItemAttributesAsset"), keys[index]);
                Set(research, "m_Attributes", data); Enable(research);
                Items.Add(keys[index], research); AddDefinition(keys[index], data);
                if (ReferenceEquals(ownBuffData, donorBuffData) || ReferenceEquals(target, donorTarget) ||
                    (string)NativeRead.Get(donorTarget, "m_UnitTypePrefix") != "G_Carrier_MP")
                    throw new InvalidOperationException("Population research isolation failed");
            }
            Runtime.Log("RHODES_POPULATION_RESEARCH_REGISTERED levels=5; sequence=25_45_65_85_105_125; costsCU=250_310_390_490_610; costsRU=0; seconds=10; nativeCommanderAttribute=524288; additivePerLevel=20; researchOwnerOnly=True; noPerFramePopulationWrites=True");
        }

        static void VerifyPopulationBuff(object data)
        {
            Array entries = NativeRead.Get(data, "m_Buffs") as Array;
            if (entries == null || entries.Length != 1) throw new InvalidOperationException("Population buff must have exactly one native commander effect");
            object entry = entries.GetValue(0);
            if (Convert.ToInt32(NativeRead.Get(entry, "m_AttributeID")) != PopulationAttributeID ||
                Convert.ToInt32(NativeRead.Get(entry, "m_Mode")) != 0 || Convert.ToInt32(NativeRead.Get(entry, "m_Value")) != 20)
                throw new InvalidOperationException("Population buff must be native Commander_PopulationCap Add20");
        }

        static void VerifyPopulationResearch()
        {
            if (!Runtime.SkirmishMode) return;
            string[] keys = PopulationResearchKeys();
            TechTreeAttributes tree = (TechTreeAttributes)NativeRead.Get(Tree, "m_Attributes");
            for (int index = 0; index < keys.Length; index++)
            {
                ResearchItemAttributes research = Registry.GetEntityType(keys[index]).Get<ResearchItemAttributes>();
                VerifyResearchBalance(research,keys[index]);
                string[] dependencies = NativeRead.Get(research, "m_ResearchDependencies") as string[];
                if (dependencies == null || dependencies.Length != (index == 0 ? 0 : 1) || (index > 0 && dependencies[0] != keys[index - 1]))
                    throw new InvalidOperationException("Private population research chain mismatch: " + keys[index]);
                Array targets = NativeRead.Get(research, "m_UnitTypeBuffs") as Array;
                if (targets == null || targets.Length != 1) throw new InvalidOperationException("Private population target count mismatch");
                object target = targets.GetValue(0);
                if ((string)NativeRead.Get(target, "m_UnitTypePrefix") != RhodesCarrierUnits.UnitKey || (bool)NativeRead.Get(target, "m_UseAsPrefix"))
                    throw new InvalidOperationException("Private population target is not the exact Rhodes carrier type");
                VerifyPopulationBuff(NativeRead.Get(NativeRead.Get(target, "m_BuffSet"), "m_Attributes"));
                if (research.TypeOfResearch!=ResearchType.Engineering||EngineeringAppearances(tree,keys[index])!=1||UpgradeAppearances(tree,keys[index])!=0)
                    throw new InvalidOperationException("Population research must appear once in Engineering only: " + keys[index]);
            }
        }
    }
}
