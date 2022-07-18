﻿using InDappledGroves.CollectibleBehaviors;
using System;
using System.Collections.Generic;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Util;
using Vintagestory.GameContent;

namespace InDappledGroves.Items.Tools
{
    class ItemIDGSaw : Item
    {
        WorldInteraction[] interactions = null;
        private double sawingTime;
        private SimpleParticleProperties woodParticles;
        private float playNextSound;

        private SkillItem[] toolModes;

        public override void OnLoaded(ICoreAPI api)
        {
            base.OnLoaded(api);
            ICoreClientAPI capi = api as ICoreClientAPI;
            toolModes = BuildSkillList();


            interactions = ObjectCacheUtil.GetOrCreate(api, "vtsawInteractions", () =>
            {
                return new WorldInteraction[] {
                    new WorldInteraction()
                        {
                            ActionLangCode = "vintertweaks:itemhelp-saw-sawwood",
                            HotKeyCode = "sprint",
                            MouseButton = EnumMouseButton.Right
                        },
                    };
            });

            
            this.toolModes = ObjectCacheUtil.GetOrCreate<SkillItem[]>(api, "idgAxeToolModes", delegate
            {
                if (capi != null)
                {
                    for (int i = 0; i < toolModes.Length; i++)
                    {
                        toolModes[i].WithIcon(capi, capi.Gui.LoadSvgWithPadding(new AssetLocation("indappledgroves:textures/icons/" + toolModes[i].Code.FirstCodePart().ToString() + ".svg"), 48, 48, 5, new int?(-1)));
                        System.Diagnostics.Debug.WriteLine(new AssetLocation("indappledgroves:textures/icons/" + toolModes[i].Code.FirstCodePart().ToString() + ".svg").ToString());
                        toolModes[i].TexturePremultipliedAlpha = false;
                    }
                };

                return toolModes;
            });

            sawingTime = 4.0;
            woodParticles = InitializeWoodParticles();
        }

        static ItemIDGSaw()
        {
            dustParticles.ParticleModel = EnumParticleModel.Quad;
            dustParticles.AddPos.Set(1, 1, 1);
            dustParticles.MinQuantity = 2;
            dustParticles.AddQuantity = 12;
            dustParticles.LifeLength = 4f;
            dustParticles.MinSize = 0.2f;
            dustParticles.MaxSize = 0.5f;
            dustParticles.MinVelocity.Set(-0.4f, -0.4f, -0.4f);
            dustParticles.AddVelocity.Set(0.8f, 1.2f, 0.8f);
            dustParticles.DieOnRainHeightmap = false;
            dustParticles.WindAffectednes = 0.5f;
        }
        public override void OnHeldInteractStart(ItemSlot slot, EntityAgent byEntity, BlockSelection blockSel, EntitySelection entitySel, bool firstEvent, ref EnumHandHandling handling)
        {
            base.OnHeldInteractStart(slot, byEntity, blockSel, entitySel, firstEvent, ref handling);

            //-- Do not process the chopping action if the player is not sneaking, or no block is selected --//
            if (!byEntity.Controls.Sprint || blockSel == null)
                return;

            Block interactedBlock = api.World.BlockAccessor.GetBlock(blockSel.Position);


            if ((interactedBlock.FirstCodePart() == "log" && interactedBlock.Variant["type"] == "placed")
                || interactedBlock.FirstCodePart() == "strippedlog"
                || (interactedBlock.FirstCodePart() == "logsection" && interactedBlock.Variant["type"] == "placed")
                || interactedBlock.FirstCodePart() == "planks"
                || interactedBlock.FirstCodePart() == "plankstairs"
                || interactedBlock.FirstCodePart() == "plankslab"
                )
            {

                //-- Stripping time modifier increases the speed at which the wood is stripped. By default, it's based on tool tier --//
                //choppingTime = api.World.Config.GetDouble("BaseBarkStrippingSpeed", 1.0) * this.Attributes["strippingTimeModifier"].AsDouble();
                //byEntity.StartAnimation("axechop");
                handling = EnumHandHandling.Handled;
            }
            playNextSound = 0f;
        }
        public override bool OnHeldInteractStep(float secondsUsed, ItemSlot slot, EntityAgent byEntity, BlockSelection blockSel, EntitySelection entitySel)
        {

            if (blockSel != null)
            {
                BlockPos pos = blockSel.Position;
                if (((int)api.Side) == 1 && playNextSound < secondsUsed && blockSel != null)
                {
                    api.World.PlaySoundAt(new AssetLocation("vintertweaks:sounds/block/saw1"), pos.X, pos.Y, pos.Z, null, false, 32, 1f);
                    playNextSound += 2f;
                }

                if (secondsUsed >= sawingTime)
                {
                    Block interactedBlock = api.World.BlockAccessor.GetBlock(blockSel.Position);
                    if (secondsUsed >= sawingTime &&
                        ((interactedBlock.FirstCodePart() == "log" && interactedBlock.Variant["type"] == "placed")
                        || interactedBlock.FirstCodePart() == "strippedlog"
                        || (interactedBlock.FirstCodePart() == "logsection" && interactedBlock.Variant["type"] == "placed")
                        || interactedBlock.FirstCodePart() == "planks"
                        || interactedBlock.FirstCodePart() == "plankstairs"
                        || interactedBlock.FirstCodePart() == "plankslab"))
                        SpawnLoot(blockSel, byEntity);
                    return false;
                }
            } else
            {
                return false;
            }
            return true;
        }

        private SkillItem[] BuildSkillList()
        {
            var skillList = new List<SkillItem>();
            foreach (var behaviour in CollectibleBehaviors)
            {
                if (behaviour is not IBehaviorVariant bwc) continue;
                foreach (SkillItem mode in bwc.GetSkillItems())
                {
                    skillList.Add(mode);
                }
            }
            return skillList.ToArray();
        }

        public override void OnHeldInteractStop(float secondsUsed, ItemSlot slot, EntityAgent byEntity, BlockSelection blockSel, EntitySelection entitySel)
        {
            byEntity.StopAnimation("axechop");
            base.OnHeldInteractStop(secondsUsed, slot, byEntity, blockSel, entitySel);
        }
        //-- Spawn firewood when the player meets/exceeds the time it takes to chop the log. Also removes the chopped log --//
        private void SpawnLoot(BlockSelection blockSel, EntityAgent byEntity)
        {
            if (api.Side == EnumAppSide.Server)
            {
                Block interactedBlock = api.World.BlockAccessor.GetBlock(blockSel.Position);

                api.World.BlockAccessor.SetBlock(0, blockSel.Position);
                api.World.BlockAccessor.MarkBlockDirty(blockSel.Position);
                //TODO: Check axe for tier and reference config for cut times and yield.
                //Remove stone recipes from the saw, implement a mason's saw with minimum tier of Bronze

                //    if ((interactedBlock.FirstCodePart() == "log" && interactedBlock.Variant["type"] == "placed")
                //            || interactedBlock.FirstCodePart() == "strippedlog"
                //            || (interactedBlock.FirstCodePart() == "logsection" && interactedBlock.Variant["type"] == "placed"))
                //    {
                //        DropScatter(interactedBlock, 12, blockSel.Position);
                //    }
                //    else if (interactedBlock.FirstCodePart() == "planks")
                //    {
                //        DropScatter(interactedBlock, 4, blockSel.Position); 
                //    }
                //    else if (interactedBlock.FirstCodePart() == "plankstairs")
                //    {
                //        DropScatter(interactedBlock, 3, blockSel.Position);
                //    }
                //    else if (interactedBlock.FirstCodePart() == "plankslab")
                //    {
                //        DropScatter(interactedBlock, 2, blockSel.Position);
                //    }
                //        if (byEntity is EntityPlayer player)
                //        this.DamageItem(api.World, byEntity, player.RightHandItemSlot, 1);
            }

        }

        //private void DropScatter(Block interactedBlock, int size, BlockPos pos)
        //{
        //    for (int i = size; i > 0; i--)
        //    {
        //        api.World.SpawnItemEntity(new ItemStack(api.World.GetItem(new AssetLocation("plank-" + interactedBlock.Variant["wood"]))), pos.ToVec3d() +
        //            new Vec3d(0, .25, 0));
        //    }
        //}

        //Particle Handlers
        private SimpleParticleProperties InitializeWoodParticles()
        {
            return new SimpleParticleProperties()
            {
                MinPos = new Vec3d(),
                AddPos = new Vec3d(),
                MinQuantity = 0,
                AddQuantity = 3,
                Color = ColorUtil.ToRgba(100, 200, 200, 200),
                GravityEffect = 1f,
                WithTerrainCollision = true,
                ParticleModel = EnumParticleModel.Quad,
                LifeLength = 0.5f,
                MinVelocity = new Vec3f(-1, 2, -1),
                AddVelocity = new Vec3f(2, 0, 2),
                MinSize = 0.07f,
                MaxSize = 0.1f,
                WindAffected = true
            };
        }

        static SimpleParticleProperties dustParticles = new SimpleParticleProperties()
        {
            MinPos = new Vec3d(),
            AddPos = new Vec3d(),
            MinQuantity = 0,
            AddQuantity = 3,
            Color = ColorUtil.ToRgba(100, 200, 200, 200),
            GravityEffect = 1f,
            WithTerrainCollision = true,
            ParticleModel = EnumParticleModel.Quad,
            LifeLength = 0.5f,
            MinVelocity = new Vec3f(-1, 2, -1),
            AddVelocity = new Vec3f(2, 0, 2),
            MinSize = 0.07f,
            MaxSize = 0.1f,
            WindAffected = true
        };

        private void SetParticleColourAndPosition(int colour, Vec3d minpos)
        {
            SetParticleColour(colour);

            woodParticles.MinPos = minpos;
            woodParticles.AddPos = new Vec3d(1, 1, 1);
        }
        private void SetParticleColour(int colour)
        {
            woodParticles.Color = colour;
        }

        public override WorldInteraction[] GetHeldInteractionHelp(ItemSlot inSlot)
        {
            return interactions;
        }
    }
}
