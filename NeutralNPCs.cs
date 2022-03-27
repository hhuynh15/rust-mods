using System.Collections.Generic;

using UnityEngine;

using Newtonsoft.Json;

namespace Oxide.Plugins
{
    [Info("Neutral NPCs", "0x89A", "2.0.0")]
    [Description("NPCs only attack if they are attacked first")]
    class NeutralNPCs : RustPlugin
    {
        private Configuration config;

        private const string canuse = "neutralnpcs.use";

        void Init() => permission.RegisterPermission(canuse, this);

        object OnNpcTarget(BaseCombatEntity entity, BasePlayer target)
        {
            if (CanTarget(entity, target)) return null;
            else return true;
        }

        bool CanBradleyApcTarget(BradleyAPC entity, BasePlayer target)
        {
            if (CanTarget(entity, target)) return true;
            else return false;
        }

        bool CanHelicopterTarget(PatrolHelicopterAI entity, BasePlayer target)
        {
            if (CanTarget(entity.helicopterBase, target)) return true;
            else return false;
        }

        #region -Helpers-

        private bool CanTarget(BaseCombatEntity entity, BasePlayer target)
        {
            return !permission.UserHasPermission(target.UserIDString, canuse) || AnimalTest(entity) || IsSelected(entity) || target.IsNpc || (entity.lastAttacker == target && HasForgotten(entity));
        }

        private bool HasForgotten(BaseCombatEntity npc)
        {
            return (Time.time - npc.lastAttackedTime) < config.forgetTime;
        }

        private bool AnimalTest(BaseEntity ent)
        {
            return config.onlyAnimls && !(ent is BaseAnimalNPC);
        }

        private bool IsSelected(BaseEntity ent)
        {
            return config.onlySelected && !config.selected.Contains(ent.ShortPrefabName);
        }

        #endregion -Helpers-

        #region -Configuration-

        private class Configuration
        {
            [JsonProperty(PropertyName = "Forget time")]
            public float forgetTime = 30f;

            [JsonProperty(PropertyName = "Only animals")]
            public bool onlyAnimls = true;

            [JsonProperty(PropertyName = "Affect only selected")]
            public bool onlySelected = false;

            [JsonProperty(PropertyName = "Selected entities")]
            public List<string> selected = new List<string>();
        }

        protected override void LoadConfig()
        {
            base.LoadConfig();
            try
            {
                config = Config.ReadObject<Configuration>();
                if (config == null) throw new System.Exception();
                SaveConfig();
            }
            catch
            {
                PrintWarning("Error loading config, using default values");
                LoadDefaultConfig();
            }
        }

        protected override void LoadDefaultConfig() => config = new Configuration();

        protected override void SaveConfig() => Config.WriteObject(config);

        #endregion
    }
}
