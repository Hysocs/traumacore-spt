using System.Linq;
using System.Reflection;
using Comfort.Common;
using EFT;
using EFT.Interactive;
using EFT.UI;
using HarmonyLib;
using SPT.Reflection.Patching;
using TraumaCore.Features.WoundInspection;

namespace TraumaCore.Patches.WoundInspection
{
    public sealed class AddCorpseWoundInspectionActionPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod() =>
            AccessTools.Method(
                typeof(InteractionContextHelper),
                nameof(InteractionContextHelper.GetAvailableActions),
                new[] { typeof(GamePlayerOwner), typeof(LootItem) });

        [PatchPostfix]
        private static void AddWoundInspectionActions(
            GamePlayerOwner owner,
            LootItem lootItem,
            ref AvailableInteractionState __result)
        {
            if ((!Plugin.EnableWoundInspection.Value &&
                    !Plugin.EnableCorpseDragging.Value) ||
                owner == null ||
                !(lootItem is Corpse corpse) ||
                corpse.IsZombieCorpse ||
                __result == null)
                return;

            if (WoundInspectionView.IsInspecting(corpse))
            {
                __result.Actions.Clear();
                return;
            }

            if (CorpseDragController.IsDragging(corpse))
            {
                __result.Actions.Clear();
                __result.Actions.Add(new InteractionAction
                {
                    Name = "STOP DRAGGING",
                    TargetName = lootItem.Name.Localized(),
                    Action = CorpseDragController.StopActiveDrag
                });
                return;
            }

            if (CorpseDragController.HasActiveDrag)
                return;

            Player corpsePlayer = FindCorpsePlayer(corpse.PlayerProfileID);
            if (corpsePlayer?.Profile == null ||
                __result.Actions.Any(action => action.Name == "INSPECT WOUNDS"))
                return;

            if (Plugin.EnableWoundInspection.Value)
            {
                __result.Actions.Add(new InteractionAction
                {
                    Name = "INSPECT WOUNDS",
                    TargetName = lootItem.Name.Localized(),
                    Action = () => WoundInspectionView.Open(
                        owner, corpse, corpsePlayer)
                });
            }
            if (Plugin.EnableCorpseDragging.Value)
            {
                __result.Actions.Add(new InteractionAction
                {
                    Name = "DRAG BODY",
                    TargetName = lootItem.Name.Localized(),
                    Action = () => CorpseDragController.Begin(owner, corpse)
                });
            }
        }

        private static Player FindCorpsePlayer(string profileId)
        {
            if (string.IsNullOrEmpty(profileId) ||
                !Singleton<GameWorld>.Instantiated)
                return null;

            return Singleton<GameWorld>.Instance.AllPlayersEverExisted?
                .FirstOrDefault(player => player?.ProfileId == profileId);
        }
    }
}
