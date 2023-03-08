using R2API;
using BepInEx;
using RoR2;
using RoR2.Skills;
using R2API.Utils;
using UnityEngine;
using UnityEngine.AddressableAssets;
using JetBrains.Annotations;
using BepInEx.Configuration;
using BepInEx.Logging;
using RoR2.UI;
using System.Security;
using System.Security.Permissions;
using System.Linq;

[module: UnverifiableCode]
[assembly: SecurityPermission(SecurityAction.RequestMinimum, SkipVerification = true)]
namespace HuntressMomentum
{

    [BepInPlugin(pluginGUID, pluginName, pluginVersion)]
	[BepInDependency(R2API.R2API.PluginGUID)]
	[NetworkCompatibility(CompatibilityLevel.EveryoneMustHaveMod, VersionStrictness.EveryoneNeedSameModVersion)]
	public class Plugin : BaseUnityPlugin {

		public const string pluginGUID = "com.doctornoodlearms.huntressmomentum";
		public const string pluginAuthor = "doctornoodlearms";
		public const string pluginName = "Huntress Momentum";
		public const string pluginVersion = "2.0.2";

		public static ManualLogSource Log;
		internal static PluginInfo pluginInfo;
		public static ConfigFile Config;
		private static AssetBundle _assetBundle;
		public static AssetBundle AssetBundle
		{
			get
			{
				if (_assetBundle == null)
					_assetBundle = AssetBundle.LoadFromFile(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(pluginInfo.Location), "huntressmomentum"));
				return _assetBundle;
			}
		}

		public static ConfigEntry<int> MaxStacks;
		public static ConfigEntry<float> BaseDuration;
		public static ConfigEntry<float> LevelDuration;

		public static BuffDef momentum;
		public static GenericSkill skillContainer;
		public static SkillDef skill;
		public static UnlockableDef unlockable;

		public static float buffTimer;
		public static float timeScalar;
		public static bool clearingBuff = false;

		private void Awake()
		{
			pluginInfo = Info;
			Log = Logger;
			Config = new ConfigFile(System.IO.Path.Combine(Paths.ConfigPath, pluginGUID + ".cfg"), true);
			MaxStacks = Config.Bind("Balance", "Required Stacks", 10, "Required stacks of momentum to guarantee crit.");
			BaseDuration = Config.Bind("Balance", "Base Duration per Stack", 1f, "Required seconds needed to gain a stack.");
			LevelDuration = Config.Bind("Balance", "Duration Multiplier per Level", 0.36f, "Momentum gain +% per level.");
			buffTimer = BaseDuration.Value; timeScalar = 1;

			RegisterBuff();
			RegisterSkill();
		}

		public static void RegisterBuff()
		{
			// Create Momentum Buff Definition
			momentum = ScriptableObject.CreateInstance<BuffDef>();

			momentum.name = "NOODLE_HUNTRESSBUFF_NAME";
			LanguageAPI.Add(momentum.name, "Momentum");
			momentum.canStack = true; momentum.isDebuff = false; momentum.isHidden = false;
			momentum.iconSprite = AssetBundle.LoadAsset<Sprite>("Assets/momentumBuff.png");

			ContentAddition.AddBuffDef(momentum);
		}

		public static void RegisterSkill()
		{
			// Create Momentum Skill Definition
			skill = ScriptableObject.CreateInstance<SkillMomentum>();

			(skill as ScriptableObject).name = "Momentum";
			skill.icon = AssetBundle.LoadAsset<Sprite>("Assets/momentumSkill.png");
			skill.activationState = new EntityStates.SerializableEntityStateType("EntityStates.Idle");
			skill.activationStateMachineName = "Body";
			skill.baseMaxStock = 1;
			skill.baseRechargeInterval = 0;
			skill.beginSkillCooldownOnSkillEnd = false; skill.isCombatSkill = false; skill.mustKeyPress = false;
			skill.skillNameToken = "NOODLE_HUNTRESSPASSIVE_NAME";
			skill.skillDescriptionToken = "NOODLE_HUNTRESSPASSIVE_DESC";
			skill.keywordTokens = new string[] { "NOODLE_HUNTRESSPASSIVE_KEYWORD" };

			LanguageAPI.Add(skill.skillNameToken, "Momentum");
			LanguageAPI.Add(skill.skillDescriptionToken, $"<style=cIsUtility>Sprinting</style> gives stacks of <style=cIsUtility>Momentum</style>. At {MaxStacks.Value} stacks, the next attack is a <style=\"cIsDamage\">Critical Strike</style>.");
			LanguageAPI.Add(skill.keywordTokens[0], $"<style=cKeywordName>Momentum</style><style=cSub>At {MaxStacks.Value} stacks, the next attack is a <style=\"cIsDamage\">Critical Strike</style>.</style>");

			ContentAddition.AddSkillDef(skill);
            GameObject huntressBody = Addressables.LoadAssetAsync<GameObject>("RoR2/Base/Huntress/HuntressBody.prefab").WaitForCompletion();

			// Create Momentum Skill Family
			SkillFamily skillFamily = ScriptableObject.CreateInstance<SkillFamily>();
			(skillFamily as ScriptableObject).name = "HuntressBodyPassiveFamily";
			skillFamily.variants = new SkillFamily.Variant[] { new SkillFamily.Variant() {
				skillDef = skill,
				unlockableDef = null,
				viewableNode = new ViewablesCatalog.Node(skill.skillNameToken, false)
			} };

			ContentAddition.AddSkillFamily(skillFamily);

			// Add Momentum Skill onto Huntress
			skillContainer = huntressBody.AddComponent<GenericSkill>();
			skillContainer._skillFamily = skillFamily;

			On.RoR2.UI.LoadoutPanelController.Rebuild += (orig, self) =>
			{
				orig(self);
				if (self.currentDisplayData.bodyIndex == BodyCatalog.FindBodyIndex("HuntressBody"))
                {
                    var passive = self.rows.FirstOrDefault(x =>
					{
						string token = x?.rowPanelTransform?.Find("LabelContainer")?.Find("SlotLabel")?.GetComponent<LanguageTextMeshController>()?.token ?? "";
						return token == "LOADOUT_SKILL_MISC" || token.ToLower() == "passive";
                    });
					if (passive != default) passive.rowPanelTransform.SetAsFirstSibling();
                }
            };

		}
		public class SkillMomentum : SkillDef
		{
			public static bool assigned = false;
            public override BaseSkillInstanceData OnAssigned([NotNull] GenericSkill skillSlot)
            {
				if (!assigned)
				{
					On.RoR2.CharacterBody.FixedUpdate += CharacterBody_FixedUpdate;
					GlobalEventManager.onCharacterLevelUp += GlobalEventManager_onCharacterLevelUp;
					RecalculateStatsAPI.GetStatCoefficients += RecalculateStatsAPI_GetStatCoefficients;
					GlobalEventManager.onServerDamageDealt += GlobalEventManager_onServerDamageDealt;
					Run.onRunDestroyGlobal += Run_onRunDestroyGlobal;
					assigned = true;
				}
				return base.OnAssigned(skillSlot);
            }

			public void CharacterBody_FixedUpdate(On.RoR2.CharacterBody.orig_FixedUpdate orig, CharacterBody self)
            {
				orig(self);
				if (self?.skillLocator != null && self.skillLocator.FindSkillByDef(skill) != null && self?.inventory != null)
                {
					if (!self.isSprinting || self.GetBuffCount(momentum) >= MaxStacks.Value) return;
					buffTimer -= Time.deltaTime * timeScalar;
					if (buffTimer <= 0)
                    {
						buffTimer = BaseDuration.Value;
						self.AddBuff(momentum);
						if (self.GetBuffCount(momentum) >= MaxStacks.Value) self.MarkAllStatsDirty();
					}
				}
			}

			public void GlobalEventManager_onCharacterLevelUp(CharacterBody body) { if (body != null) timeScalar = LevelDuration.Value * Mathf.Log(body.level) + 1; }

			public void RecalculateStatsAPI_GetStatCoefficients(CharacterBody self, RecalculateStatsAPI.StatHookEventArgs args) 
				{ if (self != null && self.GetBuffCount(momentum) >= MaxStacks.Value) args.critAdd += 100; }

			public void GlobalEventManager_onServerDamageDealt(DamageReport report)
            {
				if (report?.attackerBody == null) return;
				if (report.attackerBody.GetBuffCount(momentum) >= MaxStacks.Value && report.damageInfo.crit) 
				{
					while (report.attackerBody.HasBuff(momentum)) report.attackerBody.RemoveBuff(momentum);
					report.attackerBody.MarkAllStatsDirty();
				}
            }

			public void Run_onRunDestroyGlobal(Run _)
			{
				On.RoR2.CharacterBody.FixedUpdate -= CharacterBody_FixedUpdate;
				GlobalEventManager.onCharacterLevelUp -= GlobalEventManager_onCharacterLevelUp;
				RecalculateStatsAPI.GetStatCoefficients -= RecalculateStatsAPI_GetStatCoefficients;
				GlobalEventManager.onServerDamageDealt -= GlobalEventManager_onServerDamageDealt;
				Run.onRunDestroyGlobal -= Run_onRunDestroyGlobal;
				assigned = false;
            }
		}
	}
}

