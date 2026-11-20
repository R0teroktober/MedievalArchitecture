using AttributeRenderingLibrary;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace MedievalArchitecture
{
    /// <summary>
    /// TEST ONLY (V1): sets or removes an ARL attribute of the archway the player is looking at, e.g.
    /// "/archwaytest fill cobblestone-basalt" or "/archwaytest rock basalt". Without a value the attribute is removed.
    /// Requires the controlserver privilege. Remove or rework before the release.
    /// </summary>
    public static class ArchwayTestCommand
    {
        public static void Register(ICoreServerAPI sapi)
        {
            sapi.ChatCommands.Create("archwaytest")
                .WithDescription("Test only: set (or remove without value) an attribute of the archway you are looking at. Example: /archwaytest fill cobblestone-basalt")
                .RequiresPrivilege(Privilege.controlserver)
                .RequiresPlayer()
                .WithArgs(sapi.ChatCommands.Parsers.Word("key"), sapi.ChatCommands.Parsers.OptionalWord("value"))
                .HandleWith(args => Handle(sapi, args));
        }

        private static TextCommandResult Handle(ICoreServerAPI sapi, TextCommandCallingArgs args)
        {
            string key = ((string)args[0]).ToLowerInvariant();
            string? value = (args[1] as string)?.ToLowerInvariant();

            BlockSelection? selection = args.Caller.Player?.CurrentBlockSelection;
            if (selection == null) return TextCommandResult.Error("Look at an archway.");

            BlockPos pos = selection.Position.Copy();
            Block block = sapi.World.BlockAccessor.GetBlock(pos);
            IMultiblockOffset? offset = block as IMultiblockOffset ?? block.GetInterface<IMultiblockOffset>(sapi.World, pos);
            if (offset != null) pos = offset.GetControlBlockPos(pos);

            BlockEntity? be = sapi.World.BlockAccessor.GetBlockEntity(pos);
            BlockEntityBehaviorShapeTexturesFromAttributes? arl = be?.GetBehavior<BlockEntityBehaviorShapeTexturesFromAttributes>();
            if (be == null || arl == null) return TextCommandResult.Error("This is not an archway.");

            if (key == ArchwayMaterialRegistry.FillAttribute && !string.IsNullOrEmpty(value))
            {
                ArchwayMaterialRegistry? registry = MedievalArchitectureModSystem.GetArchwayMaterials(sapi);
                if (registry == null || !registry.TryGetFill(value, out _))
                {
                    return TextCommandResult.Error("Unknown fill \"" + value + "\". Valid ids are listed in server-debug.log (\"Archway fill ids\").");
                }
            }

            if (string.IsNullOrEmpty(value)) arl.Variants.RemoveKeys(key);
            else arl.Variants.Set(key, value);

            be.MarkDirty(true);
            return TextCommandResult.Success("Archway at " + pos + ": " + arl.Variants);
        }
    }
}
