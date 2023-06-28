﻿using System;
using System.Collections.Generic;

using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using InDappledGroves.Util;
using InDappledGroves.Blocks;
using Vintagestory.API.Common;
using Vintagestory.API.Client;
using HarmonyLib;
using Vintagestory.ServerMods;
using System.Reflection.Emit;
using Vintagestory.ServerMods.NoObf;

namespace InDappledGroves.WorldGen
{

    public class TreeHollows : ModSystem
    {
        private const int MinItems = 1;
        private const int MaxItems = 8;
        private ICoreServerAPI sapi; //The main interface we will use for interacting with Vintage Story
        private ICoreClientAPI capi;
        private int chunkSize; //Size of chunks. Chunks are cubes so this is the size of the cube.
        private ISet<string> hollowTypes; //Stores tree types that will be used for detecting trees for placing our tree hollows
        private ISet<string> stumpTypes; //Stores tree types that will be used for detecting trees for placing our tree stumps
        private IBlockAccessor chunkGenBlockAccessor; //Used for accessing blocks during chunk generation
        private IBlockAccessor worldBlockAccessor; //Used for accessing blocks after chunk generation
        private TreeLootObject[] treelootbase;
        private string[] dirs = { "north", "south", "east", "west" };
        private List<string> woods = new();
        private List<string> stumps = new();
        private readonly Harmony _harmony = new("harmoniousIDG");
        public static TreeGenComplete TreeDone = new();

        public override void Start(ICoreAPI api)
        {
            this.sapi = api as ICoreServerAPI;
            base.Start(api);
            PatchGame();
        }

        public override double ExecuteOrder()
        {
            return 0.65;
        }

        //Our mod only needs to be loaded by the server
        public override bool ShouldLoad(EnumAppSide side)
        {
            return side == EnumAppSide.Server;
        }

        public override void StartServerSide(ICoreServerAPI api)
        {
            //this.sapi = api;
            this.worldBlockAccessor = api.World.BlockAccessor;
            this.chunkSize = this.worldBlockAccessor.ChunkSize;
            woods.AddRange(IDGTreeConfig.Current.woodTypes);
            stumps.AddRange(IDGTreeConfig.Current.stumpTypes);
            this.hollowTypes = new HashSet<string>();
            this.stumpTypes = new HashSet<string>();
            this.LoadTreeTypes(this.hollowTypes);
            this.LoadStumpTypes(this.stumpTypes);
            //Registers our command with the system's command registry.
            //1.17 disable /hollow
            this.sapi.RegisterCommand("hollow", "Place a tree hollow with random items", "", this.PlaceTreeHollowInFrontOfPlayer, Privilege.controlserver);
            //Registers a delegate to be called so we can get a reference to the chunk gen block accessor
            this.sapi.Event.GetWorldgenBlockAccessor(this.OnWorldGenBlockAccessor);
            //Registers a delegate to be called when a chunk column is generating in the Vegetation phase of generation
            this.sapi.Event.ChunkColumnGeneration(this.OnChunkColumnRequest, EnumWorldGenPass.PreDone, "standard");
            this.sapi.Event.ChunkColumnLoaded += Event_ChunkColumnLoaded;
            this.sapi.Event.ServerRunPhase(EnumServerRunPhase.Shutdown, ClearTreeGen);
            TreeHollows.TreeDone.OnTreeGenCompleteEvent += NewChunkStumpAndHollowGen;
        }

        private void ClearTreeGen()
        {
            TreeHollows.TreeDone.OnTreeGenCompleteEvent -= NewChunkStumpAndHollowGen;
        }

        public override void AssetsFinalize(ICoreAPI api)
        {
            base.AssetsFinalize(api);
            this.treelootbase = CreateTreeLootList(IDGHollowLootConfig.Current.treehollowjson.ToArray());
        }

        public override void Dispose()
        {
            var harmony = new Harmony("harmoniousIDG");
            harmony.UnpatchAll("harmoniousIDG");
        }

        private void PatchGame()
        {
            Mod.Logger.Event("Applying Harmony patches");
            var harmony = new Harmony("harmoniousIDG");
            var original = typeof(TreeGen).GetMethod("GrowTree");
            var patches = (Harmony.GetPatchInfo(original));
            if (patches != null && patches.Owners.Contains("harmoniousIDG"))
            {
                return;
            }
            harmony.PatchAll();
        }




#pragma warning disable IDE0051 // Remove unused private members
        private void UnPatchGame()

        {
            Mod.Logger.Event("Unapplying Harmony patches");

            _harmony.UnpatchAll();
        }

        private void Event_ChunkColumnLoaded(Vec2i chunkCoord, IWorldChunk[] chunks)
        {
            if (IDGTreeConfig.Current.RunTreeGenOnChunkReload)
            {
                foreach (IWorldChunk chunk in chunks)
                {
                    if (chunk.Empty /*|| chunk.GetModdata<bool>("hasIDGLoaded", false) == true*/) continue;

                    IMapChunk mc = sapi.World.BlockAccessor.GetMapChunk(chunkCoord);
                    if (mc == null) continue;   //this chunk isn't actually loaded, no need to examine it.

                    if (chunk.GetModdata<bool>("hasIDGLoaded", false) == true) break;

                    runExistingWorldTreeGen(chunk, new BlockPos(chunkCoord.X, 0, chunkCoord.Y));
                    chunk.SetModdata<bool>("hasIDGLoaded", true);
                    return;
                }
            }
        }

        private void LoadTreeTypes(ISet<string> treeTypes)
        {
            foreach (var variant in this.woods)
            {
                treeTypes.Add($"log-grown-" + variant + "-ud");
            }
        }

        private void LoadStumpTypes(ISet<string> stumpTypes)
        {

            foreach (var variant in this.stumps)
            {
                stumpTypes.Add($"log-grown-" + variant + "-ud");
            }
        }

        /// <summary>
        /// Stores the chunk gen thread's IBlockAccessor for use when generating tree hollows during chunk gen. This callback
        /// is necessary because chunk loading happens in a separate thread and it's important to use this block accessor
        /// when placing tree hollows during chunk gen.
        /// </summary>
        private void OnWorldGenBlockAccessor(IChunkProviderThread chunkProvider)
        {
            this.chunkGenBlockAccessor = chunkProvider.GetBlockAccessor(true);
        }

        /// <summary>
        /// Called when a number of chunks have been generated. For each chunk we first determine if we should place a tree hollow
        /// and if we should we then loop through each block to find a tree. When one is found we place the block.
        /// </summary>
        private void OnChunkColumnRequest(IChunkColumnGenerateRequest request)
        {
            var chunks = request.Chunks;
            var chunkX = request.ChunkX;
            var chunkZ = request.ChunkZ;
            //Debug.WriteLine("Entering the death loop for chunk " + chunkX + " " + chunkZ);
            for (var i = 0; i < chunks.Length; i++)
            {
                if (IDGTreeConfig.Current.RunTreeGenOnChunkReload)
                {
                    runExistingWorldTreeGen(chunks[i], new BlockPos(chunkX, 0, chunkZ));
                }
                chunks[i].SetModdata<bool>("hasIDGLoaded", true);
            }
        }

        private void runExistingWorldTreeGen(IWorldChunk chunk, BlockPos pos)
        {
            var hollowsPlacedCount = 0;

            var blockPos = new BlockPos();
            //arbitrarily limit x axis scan for performance reasons (/4)
            for (var x = 0; x < this.chunkSize; x++)
            {
                //arbitrarily limit z axis scan for performance reasons (/4)
                for (var z = 0; z < this.chunkSize; z++)
                {
                    blockPos.X = (pos.X * this.chunkSize) + x;
                    blockPos.Z = (pos.Z * this.chunkSize) + z;
                    blockPos.Y = this.worldBlockAccessor.GetTerrainMapheightAt(blockPos) + 1;
                    Block curBlock = this.chunkGenBlockAccessor.GetBlock(blockPos, BlockLayersAccess.Default);
                    if (sapi.ModLoader.IsModEnabled("primitivesurvival"))
                    {
                        if (IsStumpLog(curBlock))
                        {
                            PlaceTreeStump(this.chunkGenBlockAccessor, blockPos);
                        }
                        continue;
                    }

                    if (!IsStumpLog(curBlock) || this.worldBlockAccessor.GetBlock(blockPos.DownCopy()).Fertility > 0) continue;
                    if (hollowsPlacedCount < IDGTreeConfig.Current.TreeHollowsMaxPerChunk && (sapi.World.Rand.NextDouble() < 0.2))
                    {
                        var hollowWasPlaced = this.PlaceTreeHollow(this.chunkGenBlockAccessor, blockPos);
                        if (hollowWasPlaced)
                        {
                            hollowsPlacedCount++;
                            continue;
                        }
                    }
                    PlaceTreeStump(this.chunkGenBlockAccessor, blockPos);

                    if (ShouldPlaceHollow() && hollowsPlacedCount < IDGTreeConfig.Current.TreeHollowsMaxPerChunk && IsTreeLog(curBlock))
                    {
                        var hollowLocation = this.TryGetHollowLocation(blockPos);
                        if (hollowLocation == null) continue;
                        var hollowWasPlaced = this.PlaceTreeHollow(this.chunkGenBlockAccessor, hollowLocation);
                        if (hollowWasPlaced)
                        {
                            hollowsPlacedCount++;
                        }
                    }

                }

            }
        }

        private void NewChunkStumpAndHollowGen(BlockPos pos)
        {
            pos.UpCopy();
            sapi.World.BlockAccessor.WalkBlocks(pos.AddCopy(-1, 0, -1), pos.AddCopy(1, 0, 1), delegate (Block block, int x, int y, int z)
            {
                System.Diagnostics.Debug.WriteLine("Hit");
                if (IsStumpLog(block))
                {
                    PlaceTreeStump(this.chunkGenBlockAccessor, pos);
                    System.Diagnostics.Debug.WriteLine("Hit");
                }
            });
        }

        // Returns the location to place the hollow if the given world coordinates is a tree, null if it's not a tree.
        private BlockPos TryGetHollowLocation(BlockPos pos)
        {
            var block = this.chunkGenBlockAccessor.GetBlock(pos, BlockLayersAccess.Default);
            if (this.IsTreeLog(block))
            {
                for (var posY = pos.Y; posY >= 0; posY--)
                {
                    while (pos.Y-- > 0)
                    {
                        var underBlock = this.chunkGenBlockAccessor.GetBlock(pos, BlockLayersAccess.Default);
                        if (this.IsTreeLog(underBlock))
                        {
                            continue;
                        }
                        return pos.UpCopy();
                    }
                }
            }
            return null;
        }

        private bool IsTreeLog(Block block)
        {
            return this.hollowTypes.Contains(block.Code.Path);
        }

        private bool IsStumpLog(Block block)
        {
            return this.stumpTypes.Contains(block.Code.Path);
        }

        // Delegate for /hollow command. Places a treehollow 2 blocks in front of the player
        private void PlaceTreeHollowInFrontOfPlayer(IServerPlayer player, int groupId, CmdArgs args)
        {
            this.PlaceTreeHollow(this.sapi.World.BlockAccessor, player.Entity.Pos.HorizontalAheadCopy(2).AsBlockPos);
        }

        // Places a tree stump at the given world coordinates using the given IBlockAccessor
        private bool PlaceTreeStump(IBlockAccessor blockAccessor, BlockPos pos)
        {

            var treeBlock = blockAccessor.GetBlock(pos, BlockLayersAccess.Default);
            //Debug.WriteLine("Will replace:" + treeBlock.Code.Path);
            var stumpType = "pine";

            if (treeBlock.FirstCodePart() == "log")
            {
                stumpType = treeBlock.FirstCodePart(2);
            }

            var withPath = "indappledgroves:treestump-grown-" + stumpType + "-" + this.dirs[this.sapi.World.Rand.Next(4)];
            //Debug.WriteLine("With: " + withPath);
            var withBlockID = this.sapi.WorldManager.GetBlockId(new AssetLocation(withPath));
            var withBlock = blockAccessor.GetBlock(withBlockID);
            blockAccessor.SetBlock(0, pos);
            if (withBlock.TryPlaceBlockForWorldGen(blockAccessor, pos, BlockFacing.UP, null)) return true;
            return false;
        }

        // Places a tree hollow filled with random items at the given world coordinates using the given IBlockAccessor
        private bool PlaceTreeHollow(IBlockAccessor blockAccessor, BlockPos pos)
        {

            //consider moving it upwards
            var upCount = this.sapi.World.Rand.Next(2, 8);
            var upCandidateBlock = blockAccessor.GetBlock(pos.UpCopy(upCount), BlockLayersAccess.Default);

            if (upCandidateBlock.FirstCodePart() == "log")
            { pos = pos.UpCopy(upCount); }

            var treeBlock = blockAccessor.GetBlock(pos, BlockLayersAccess.Default);
            //Debug.WriteLine("Will replace:" + treeBlock.Code.Path);
            var woodType = "pine";

            if (treeBlock.FirstCodePart() == "log")
            {
                woodType = treeBlock.FirstCodePart(2);
            }

            var hollowType = "up";
            if (this.sapi.World.Rand.Next(2) == 1)
            { hollowType = "up2"; }
            var belowBlock = blockAccessor.GetBlock(pos.DownCopy(), BlockLayersAccess.Default);
            if (belowBlock.Fertility > 0) //fertile ground below?
            {
                if (this.sapi.World.Rand.Next(2) == 1)
                { hollowType = "base"; }
                else
                { hollowType = "base2"; }
            }

            var withPath = "indappledgroves:treehollowgrown-" + hollowType + "-" + woodType + "-" + this.dirs[this.sapi.World.Rand.Next(4)];
            var withBlockID = this.sapi.WorldManager.GetBlockId(new AssetLocation(withPath));
            var withBlock = blockAccessor.GetBlock(withBlockID);
            if (withBlock.TryPlaceBlockForWorldGen(blockAccessor, pos, BlockFacing.UP, null))
            {
                var block = blockAccessor.GetBlock(pos, BlockLayersAccess.Default) as BlockTreeHollowGrown;
                if (block.EntityClass != null)
                {
                    if (block.EntityClass == withBlock.EntityClass)
                    {
                        blockAccessor.SpawnBlockEntity(block.EntityClass, pos);
                        var be = blockAccessor.GetBlockEntity(pos);
                        //if (be is BETreeHollowGrown && sapi != null)
                        //{
                        //    var hollow = blockAccessor.GetBlockEntity(pos) as BETreeHollowGrown;
                        //    ItemStack[] lootStacks = GetTreeLoot(treelootbase, pos);
                        //    if (lootStacks != null) AddItemStacks(hollow, lootStacks);
                        //}
                    }
                }
                return true;
            }
            else
            { return false; }
        }

        private bool ShouldPlaceHollow()
        {
            var randomNumber = this.sapi.World.Rand.Next(0, 100);
            return randomNumber > 0 && randomNumber <= IDGTreeConfig.Current.TreeHollowsSpawnProbability * 100;
        }

        //Adds the given list of ItemStacks to the first slots in the given hollow.
        public void AddItemStacks(IBlockEntityContainer hollow, ItemStack[] itemStacks)
        {
            var slotNumber = 0;
            if (itemStacks != null)
            {
                while (slotNumber < sapi.World.Rand.Next(hollow.Inventory.Count - 1))
                {
                    var slot = hollow.Inventory[slotNumber];
                    slot.Itemstack = itemStacks[sapi.World.Rand.Next(0, itemStacks.Length - 1)];
                    slotNumber++;
                }
            }
        }

        //public void RegenItemStacks(ICoreAPI api, BETreeHollowGrown hollow, JsonObject[] itemStacks, BlockPos pos)
        //{
        //    if (api.Side == EnumAppSide.Client)
        //    {
        //        ItemStack[] lootStacks = ConvertTreeLoot(itemStacks, pos);
        //        if (lootStacks != null) AddItemStacks(hollow, lootStacks);

        //        if (itemStacks != null)
        //        {
        //            var slotNumber = 0;
        //            while (slotNumber < hollow.Inventory.Count - 1)
        //            {
        //                var slot = hollow.Inventory[slotNumber].Itemstack;
        //                slot = lootStacks[api.World.Rand.Next(0, itemStacks.Length - 1)];
        //                slotNumber++;
        //            }
        //        }
        //    }
        //}

        private ItemStack[] GetTreeLoot(TreeLootObject[] treeLoot, BlockPos pos)
        {
            List<ItemStack> lootList = null;
            if (sapi != null)
            {
                ClimateCondition climate = sapi.World.BlockAccessor.GetClimateAt(pos);

                foreach (TreeLootObject lootItem in treeLoot)
                {
                    if (lootList == null && ClimateLootFilter(lootItem, pos))
                    {
                        lootItem.bstack.Resolve(sapi.World, "treedrop: ", lootItem.bstack.Code);
                        lootList = new();
                        lootList.Add(lootItem.bstack.GetNextItemStack());
                        continue;
                    }
                    if (lootList != null && ClimateLootFilter(lootItem, pos) && lootList.Count >= 0 && lootList.Count <= 8)
                    {
                        lootItem.bstack.Resolve(sapi.World, "treedrop: ", lootItem.bstack.Code);

                        lootList.Add(lootItem.bstack.GetNextItemStack());
                    }
                }
            }

            return lootList == null ? null : lootList.ToArray();
        }

        private TreeLootObject[] CreateTreeLootList(JsonObject[] treeLoot)
        {
            List<TreeLootObject> treelootlist = new();
            foreach (JsonObject lootStack in treeLoot)
            {
                TreeLootObject obj = new TreeLootObject(lootStack);
                if (obj.bstack.Resolve(sapi.World, "treedrop: ", obj.bstack.Code))
                {
                    treelootlist.Add(obj);
                }
            }
            return treelootlist.Count > 0 ? treelootlist.ToArray() : null;
        }

        private bool ClimateLootFilter(TreeLootObject obj, BlockPos pos)
        {
            ClimateCondition local = sapi.World.BlockAccessor.GetClimateAt(pos);

            return (local.ForestDensity >= obj.cReqs.minForest)
            && (local.ForestDensity <= obj.cReqs.maxForest)
            && (local.ShrubDensity >= obj.cReqs.minShrub)
            && (local.ShrubDensity <= obj.cReqs.maxShrub)
            && (local.Rainfall >= obj.cReqs.minRain)
            && (local.Rainfall <= obj.cReqs.maxRain)
            && (local.Temperature >= obj.cReqs.minTemperature)
            && (local.Temperature <= obj.cReqs.maxTemperature)
            && ((((int)sapi.World.Calendar.GetSeason(pos)) == obj.cReqs.season) ||
                obj.cReqs.season == 4);
        }

        public static List<String> treehollowloot { get; set; } = new()
        {
            @"{""dropStack"": {""type"":""block"", ""code"": ""game:mushroom-fieldmushroom-normal"", ""quantity"": { ""avg"": 0.5, ""var"": 2, ""dist"": ""strongerinvexp""} }, ""dropReqs"": {}}",
            @"{""dropStack"": {""type"":""item"", ""code"": ""game:fruit-yellowapple"", ""quantity"": {""avg"": 0.5, ""var"": 2, ""dist"": ""strongerinvexp""}}, ""dropReqs"": {}}",
            @"{""dropStack"": {""type"":""item"", ""code"": ""game:fruit-redapple"", ""quantity"": {""avg"": 0.5, ""var"": 2, ""dist"": ""strongerinvexp""}}, ""dropReqs"": {}}",
            @"{""dropStack"": {""type"":""item"", ""code"": ""game:drygrass"", ""quantity"": {""avg"": 0.5, ""var"": 2, ""dist"": ""strongerinvexp""}}, ""dropReqs"": {}}",
            @"{""dropStack"": {""type"":""item"", ""code"": ""game:fruit-cherry"", ""quantity"": {""avg"": 0.5, ""var"": 2, ""dist"": ""strongerinvexp""}}, ""dropReqs"": {}}",
            @"{""dropStack"": {""type"":""item"", ""code"": ""game:insect-grub"", ""quantity"": {""avg"": 0.5, ""var"": 2, ""dist"": ""strongerinvexp""}}, ""dropReqs"": {}}",
            @"{""dropStack"": {""type"":""item"", ""code"": ""game:insect-termite"", ""quantity"": {""avg"": 0.5, ""var"": 2, ""dist"": ""strongerinvexp""}}, ""dropReqs"": {}}",
            @"{""dropStack"": {""type"":""item"", ""code"": ""game:cattailroot"", ""quantity"": {""avg"": 0.5, ""var"": 2, ""dist"": ""strongerinvexp""}}, ""dropReqs"": {}}",
            @"{""dropStack"": {""type"":""item"", ""code"": ""game:cattailtops"", ""quantity"": {""avg"": 0.5, ""var"": 2, ""dist"": ""strongerinvexp""}}, ""dropReqs"": {}}",
            @"{""dropStack"": {""type"":""item"", ""code"": ""game:honeycomb"", ""quantity"": {""avg"": 0.5, ""var"": 2, ""dist"": ""strongerinvexp""}}, ""dropReqs"": {}}",
            @"{""dropStack"": {""type"":""item"", ""code"": ""game:rot"", ""quantity"": {""avg"": 0.5, ""var"": 2, ""dist"": ""strongerinvexp""}}, ""dropReqs"": {}}",
            @"{""dropStack"": {""type"":""item"", ""code"": ""game:stick"", ""quantity"": {""avg"": 0.5, ""var"": 2, ""dist"": ""strongerinvexp""}}, ""dropReqs"": {}}",
            @"{""dropStack"": {""type"":""item"", ""code"": ""game:stone-limestone"", ""quantity"": {""avg"": 0.5, ""var"": 2, ""dist"": ""strongerinvexp""}}, ""dropReqs"": {}}",
            @"{""dropStack"": {""type"":""item"", ""code"": ""game:arrow-flint"", ""quantity"": {""avg"": 0.5, ""var"": 2, ""dist"": ""strongerinvexp""}}, ""dropReqs"": {}}",
            @"{""dropStack"": {""type"":""item"", ""code"": ""game:gear-rusty"", ""quantity"": {""avg"": 0.5, ""var"": 1, ""dist"": ""strongerinvexp""}}, ""dropReqs"": {}}",
            @"{""dropStack"": {""type"":""block"", ""code"": ""game:mushroom-fieldmushroom-normal"", ""quantity"": {""avg"": 0.5, ""var"": 2, ""dist"": ""strongerinvexp""}}, ""dropReqs"": {}}",
            @"{""dropStack"": {""type"":""block"", ""code"": ""game:mushroom-commonmorel-normal"", ""quantity"": {""avg"": 0.5, ""var"": 2, ""dist"": ""strongerinvexp""}}, ""dropReqs"": {}}",
            @"{""dropStack"": {""type"":""block"", ""code"": ""game:mushroom-almondmushroom-normal"", ""quantity"": {""avg"": 0.5, ""var"": 2, ""dist"": ""strongerinvexp""}}, ""dropReqs"": {}}",
            @"{""dropStack"": {""type"":""block"", ""code"": ""game:mushroom-orangeoakbolete-normal"", ""quantity"": {""avg"": 0.5, ""var"": 2, ""dist"": ""strongerinvexp""}}, ""dropReqs"": {}}",
            @"{""dropStack"": {""type"":""block"", ""code"": ""game:mushroom-flyagaric-harvested"", ""quantity"": {""avg"": 0.5, ""var"": 2, ""dist"": ""strongerinvexp""}}, ""dropReqs"": {}}"
        };

    }
    internal class TreeLootObject
    {
        public BlockDropItemStack bstack { get; }
        public ClimateRequirements cReqs { get; }

        public TreeLootObject() { }
        public TreeLootObject(JsonObject treeLoot)
        {
            bstack = treeLoot["dropStack"].AsObject<BlockDropItemStack>();
            cReqs = treeLoot["dropReqs"].AsObject<ClimateRequirements>();
        }
    }

    internal class ClimateRequirements
    {
        public float minForest { get; set; } = 0.0f;
        public float maxForest { get; set; } = 1f;
        public float minRain { get; set; } = 0.0f;

        public float maxRain { get; set; } = 1f;
        public float minShrub { get; set; } = 0.0f;
        public float maxShrub { get; set; } = 1f;
        public float minTemperature { get; set; } = -100.0f;
        public float maxTemperature { get; set; } = 200f;
        public int season { get; set; } = 4;
        public ClimateRequirements() { }

    }

    public delegate void TreeGenCompleteDelegate(BlockPos pos);
    //subscriber class

    public class TreeGenComplete
    {

        public event TreeGenCompleteDelegate OnTreeGenCompleteEvent;

        public void OnTreeGenComplete(BlockPos pos)
        {
            OnTreeGenCompleteEvent?.Invoke(pos);
        }

    }

    [HarmonyPatch]
    [HarmonyPatch(typeof(TreeGen), "GrowTree")]   
    class TreeGenPatches
    {
      

        [HarmonyPostfix]
        [HarmonyPatch(typeof(TreeGen), "GrowTree")]
        static void Patch_TreeGen_GrowTree_Postfix(BlockPos pos)
        {
            TreeHollows.TreeDone.OnTreeGenComplete(pos);
        }

        //[HarmonyPatch]
        //public class OriginalClass
        //{
        //    [HarmonyPrefix]
        //    [HarmonyPatch(typeof(OriginalClass), "OrginalPrivateMethod")]
        //    private static bool Harmony_OriginalClass_OrginalPrivateMethod_Prefix(string strParam, ref string ___privateString)
        //    {
        //        ___privateString = strParam.ToUpper();
        //    }
        //}
    }

}