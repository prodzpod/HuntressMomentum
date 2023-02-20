using R2API;
using BepInEx;
using RoR2;
using RoR2.Skills;
using R2API.Utils;
using UnityEngine;
using UnityEngine.AddressableAssets;

namespace HuntressMomentum {

	[BepInPlugin(pluginGUID, pluginName, pluginVersion)]
	[BepInDependency(R2API.R2API.PluginGUID)]
	[R2APISubmoduleDependency(
		nameof(LanguageAPI),
		nameof(RecalculateStatsAPI),
		nameof(ContentAddition)
	)]
	[NetworkCompatibility(CompatibilityLevel.EveryoneMustHaveMod, VersionStrictness.EveryoneNeedSameModVersion)]
	public class Plugin : BaseUnityPlugin {

		public const string pluginGUID = "com.doctornoodlearms.huntressmomentum";
		public const string pluginAuthor = "doctornoodlearms";
		public const string pluginName = "Huntress Momentum";
		public const string pluginVersion = "1.1.1";

		BuffDef momentum;
		SkillDef skill;
		UnlockableDef unlockable;

		readonly int maxStacks = 10;
		readonly float timerDuration = 1.00f;

		float buffTimer = 1.00f;
		float timeScalar = 1.00f;
		bool clearingBuff = false;

		CharacterBody player = null;

		private void Awake() {

			// Create Momentum Buff Definition
			momentum = ScriptableObject.CreateInstance<BuffDef>();

			momentum.name = "NOODLE_HUNTRESSBUFF_NAME";
			momentum.canStack = true;
			momentum.isDebuff = false;
			momentum.isHidden = false;
			momentum.iconSprite = Addressables.LoadAssetAsync<Sprite>("RoR2/Base/UI/texBasicArrowRight.png").WaitForCompletion();

			ContentAddition.AddBuffDef(momentum);

			// Create Momentum Skill Definition
			skill = ScriptableObject.CreateInstance<SkillDef>();

			(skill as ScriptableObject).name = "Momentum";
			skill.icon = Addressables.LoadAssetAsync<Sprite>("RoR2/Base/UI/texBasicArrowRight.png").WaitForCompletion();
			skill.activationState = new EntityStates.SerializableEntityStateType("EntityStates.Idle");
			skill.activationStateMachineName = "Body";
			skill.baseMaxStock = 1;
			skill.baseRechargeInterval = 0;
			skill.beginSkillCooldownOnSkillEnd = false;
			skill.isCombatSkill = false;
			skill.mustKeyPress = false;
			skill.skillNameToken = "NOODLE_HUNTRESSPASSIVE_NAME";
			skill.skillDescriptionToken = "NOODLE_HUNTRESSPASSIVE_DESC";

			LanguageAPI.Add(skill.skillNameToken, "Momentum");
			LanguageAPI.Add(skill.skillDescriptionToken, $"Sprinting gives stacks of momentum at {maxStacks} stacks the next attack will crit");

			ContentAddition.AddSkillDef(skill);

			// Create Momentum Skill Family
			SkillFamily skillFamily = ScriptableObject.CreateInstance<SkillFamily>();
			(skillFamily as ScriptableObject).name = "HuntressBodyPassiveFamily";
			skillFamily.variants = new SkillFamily.Variant[1];
			skillFamily.variants[0] = new SkillFamily.Variant {

				skillDef = skill,
				unlockableName = "",
				viewableNode = new ViewablesCatalog.Node(skill.skillNameToken, false)
			};

			ContentAddition.AddSkillFamily(skillFamily);

			// Add Momentum Skill onto Huntress
			GameObject huntressBody = Addressables.LoadAssetAsync<GameObject>("RoR2/Base/Huntress/HuntressBody.prefab").WaitForCompletion();
			GenericSkill passiveSkill = huntressBody.AddComponent<GenericSkill>();
			passiveSkill.SetFieldValue("_skillFamily", skillFamily);

			On.RoR2.GlobalEventManager.OnHitAll += GlobalEventManager_OnHitAll;
			On.RoR2.GenericSkill.Start += GenericSkill_Start;
			On.RoR2.GlobalEventManager.OnCharacterLevelUp += GlobalEventManager_OnCharacterLevelUp;

			RecalculateStatsAPI.GetStatCoefficients += GetStatCoefficients;
		}

		// Check if the player has the Momentum Skill
		private void GenericSkill_Start(On.RoR2.GenericSkill.orig_Start orig, GenericSkill self) {

			orig(self);
			if(self.skillDef == skill) {

				player = self.characterBody;
			}
		}

		private void GlobalEventManager_OnCharacterLevelUp(On.RoR2.GlobalEventManager.orig_OnCharacterLevelUp orig, CharacterBody characterBody) {

			if(characterBody != null) {

				timeScalar = 0.36f * Mathf.Log(characterBody.level) + 1;
			}
		}

		// Gurantees Crit
		private void GetStatCoefficients(CharacterBody sender, RecalculateStatsAPI.StatHookEventArgs args) {

			if(sender) {

				// Check if the player has enough stacks
				if(sender.GetBuffCount(momentum) >= maxStacks) {

					args.critAdd += 100;
				}
			}
		}

		private void FixedUpdate() {

			if(player) {
				
				if(player.isSprinting) {

					// Recalculate crit on the player if they have enough stacks
					if(player.GetBuffCount(momentum) >= maxStacks) {

						player.RecalculateStats();
					}
					else {

						// Increments the timer
						if(buffTimer > 0) {

							buffTimer -= Time.deltaTime * timeScalar;
						}
						else {

							// Resets the timer and gives the player a stack
							buffTimer = timerDuration;
							player.AddBuff(momentum);
						}
					}
				}
			}
		}

		private void GlobalEventManager_OnHitAll(On.RoR2.GlobalEventManager.orig_OnHitAll orig, GlobalEventManager self, DamageInfo damageInfo, GameObject hitObject) {

			if(damageInfo.attacker != null) {

				CharacterBody attackerBody = damageInfo.attacker.GetComponent<CharacterBody>();
				if(attackerBody.HasBuff(momentum) && damageInfo.crit && !clearingBuff) {

					ClearMomentum(attackerBody);
				}
			}

			orig.Invoke(self, damageInfo, hitObject);
		}

		/// Removes all of Momentum stacks DONT TOUCH
		private void ClearMomentum(CharacterBody body) {

			if(body) {

				if(body.HasBuff(momentum)) {

					clearingBuff = true;
					for(int i = body.GetBuffCount(momentum); i > 0; i--) {

						body.RemoveBuff(momentum);
					}

					clearingBuff = false;
				}
			}
		}

		private void Log(string message) {

			Logger.Log(BepInEx.Logging.LogLevel.Debug, message);
		}
	}
}

