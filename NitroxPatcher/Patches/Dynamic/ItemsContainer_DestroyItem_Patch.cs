using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using NitroxClient.Communication.Abstract;
using NitroxClient.MonoBehaviours;
using NitroxModel.DataStructures;
using NitroxModel.Helper;
using NitroxModel.Packets;
using NitroxPatcher.PatternMatching;
using static System.Reflection.Emit.OpCodes;

namespace NitroxPatcher.Patches.Dynamic;

/// <summary>
/// When the player crafts items the game will leverage this API to select a pickupable
/// from their inventory and delete it.  We want to let the server know that the item
/// was successfully deleted.
/// </summary>
public class ItemsContainer_DestroyItem_Patch : NitroxPatch, IDynamicPatch
{
    internal static readonly MethodInfo TARGET_METHOD = Reflect.Method((ItemsContainer t) => t.DestroyItem(default(TechType)));

    private static readonly InstructionsPattern removeItemPattern = new()
    {
        Ldarg_0,
        Ldarg_1,
        Reflect.Method((ItemsContainer container) => container.RemoveItem(default(TechType))),
        { Stloc_0, "NotifyServer" }
    };

    public static IEnumerable<CodeInstruction> Transpiler(MethodBase original, IEnumerable<CodeInstruction> instructions)
    {
        static IEnumerable<CodeInstruction> InsertNotifyServerCall(string label, CodeInstruction instruction)
        {
            // After the call to RemoveItem (and storing the return value) we want to call our callback method
            if (label.Equals("NotifyServer"))
            {
                yield return new(Ldloc_0);
                yield return new(Call, Reflect.Method(() => Callback(default)));
            }
        }

        return instructions.Transform(removeItemPattern, InsertNotifyServerCall);
    }

    private static void Callback(Pickupable pickupable)
    {
        if (pickupable)
        {
            NitroxId id = NitroxEntity.GetId(pickupable.gameObject);
            Resolve<IPacketSender>().Send(new EntityDestroyed(id));
        }
    }

    public override void Patch(Harmony harmony)
    {
        PatchTranspiler(harmony, TARGET_METHOD);
    }
}
