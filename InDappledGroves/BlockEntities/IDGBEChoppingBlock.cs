﻿using System.Collections.Generic;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;
using static InDappledGroves.Util.IDGRecipeNames;

namespace InDappledGroves.BlockEntities
{
    class IDGBEChoppingBlock : BlockEntityDisplay
    {
		public override InventoryBase Inventory { get; }
		public override string InventoryClassName => "choppingblock";
        public override string AttributeTransformCode => "onDisplayTransform";

		static List<ChoppingRecipe> choppingRecipes = IDGRecipeRegistry.Loaded.ChoppingRecipes;

		public IDGBEChoppingBlock()
		{
			Inventory = new InventoryGeneric(1, "choppingblock-slot", null, null);
			meshes = new MeshData[1];

		}

		public override void Initialize(ICoreAPI api)
		{
			base.Initialize(api);
			this.capi = (api as ICoreClientAPI);
			if (this.capi != null)
			{
				this.updateMeshes();
			}
		}
		public ItemSlot InputSlot
		{
			get { return Inventory[0]; }
		}

		internal bool OnInteract(IPlayer byPlayer)
		{
			ItemSlot activeHotbarSlot = byPlayer.InventoryManager.ActiveHotbarSlot;

			//If The Players Hand Is Empty
			if (activeHotbarSlot.Empty)
			{
				return this.TryTake(byPlayer);
			}

			//If the player is holding an object, and the inventory of the block is full,
			//or if the item in the players hand does not have attributes, or the cuttable attribute is false

			CollectibleObject collectible = activeHotbarSlot.Itemstack.Collectible;
			JsonObject attributes = collectible.Attributes;
			if ((!activeHotbarSlot.Empty && !Inventory.Empty) || attributes == null || !collectible.Attributes["woodworkingProps"]["idgChoppingBlockProps"]["choppable"].AsBool(false)) return true;		

            ItemStack itemstack = activeHotbarSlot.Itemstack;
			AssetLocation assetLocation;
			if (itemstack == null)
			{
				assetLocation = null;
			}
			else
			{
				Block block = itemstack.Block;
				if (block == null)
				{
					assetLocation = null;
				}
				else
				{
					BlockSounds sounds = block.Sounds;
					assetLocation = (sounds?.Place);
				}
			}
			AssetLocation assetLocation2 = assetLocation;
			if (this.TryPut(activeHotbarSlot))
			{	 
				this.Api.World.PlaySoundAt(assetLocation2 ?? new AssetLocation("sounds/player/build"), byPlayer.Entity, byPlayer, true, 16f, 1f);
                updateMeshes();
				return true;
			}
			return false;
		}

		private bool TryPut(ItemSlot slot)
		{
			for (int i = 0; i < Inventory.Count; i++)
			{
                if (this.Inventory[i].Empty)
                {
                    int num3 = slot.TryPutInto(this.Api.World, this.Inventory[i], 1);
                    this.updateMeshes();
                    base.MarkDirty(true, null);
                    return num3 > 0;
                }
            }
			return false;
		}

		private bool TryTake(IPlayer byPlayer)
		{
			for (int i = 0; i < Inventory.Count; i++)
			{
				if (!this.Inventory[i].Empty)
				{
					ItemStack itemStack = this.Inventory[i].TakeOut(1);
					if (byPlayer.InventoryManager.TryGiveItemstack(itemStack, false))
					{
						Block block = itemStack.Block;
						AssetLocation assetLocation;
						if (block == null)
						{
							assetLocation = null;
						}
						else
						{
							BlockSounds sounds = block.Sounds;
							assetLocation = (sounds?.Place);
						}
						AssetLocation assetLocation2 = assetLocation;
						this.Api.World.PlaySoundAt(assetLocation2 ?? new AssetLocation("sounds/player/build"), byPlayer.Entity, byPlayer, true, 16f, 1f);
					}
					if (itemStack.StackSize > 0)
					{
						this.Api.World.SpawnItemEntity(itemStack, this.Pos.ToVec3d().Add(0.5, 0.5, 0.5), null);
					}
					base.MarkDirty(true, null);
					this.updateMeshes();
					return true;
				}
			}
			return false;
		}

		public override void TranslateMesh(MeshData mesh, int index)
		{
			JsonObject Translations = this.Inventory[index].Itemstack.Collectible?.Attributes["woodworkingProps"]["idgChoppingBlockProps"]["idgChoppingBlockTranslate"];

			float x = 0f;
			float y = 0.375f;
			float z = 0.45f;

            if (Translations.Exists)
            {
                x = Translations["x"].Exists ? Translations["x"].AsFloat() : x;
                y = Translations["y"].Exists ? Translations["y"].AsFloat() : y;
                z = Translations["z"].Exists ? Translations["z"].AsFloat() : z;

                Vec4f offset = mat.TransformVector(new Vec4f(x, y, z, 0));
				mesh.Translate(offset.XYZ);
            }

        }

		protected override MeshData genMesh(ItemStack stack)
		{
			MeshData meshData;
			if (stack.Collectible is IContainedMeshSource containedMeshSource)
			{
				meshData = containedMeshSource.GenMesh(stack, this.capi.BlockTextureAtlas, this.Pos);
				meshData.Rotate(new Vec3f(0.5f, 0.5f, 0.5f), 0f, base.Block.Shape.rotateY * 0.017453292f, 0f);
			}
			else
			{
				this.nowTesselatingObj = stack.Collectible;
				this.nowTesselatingShape = capi.TesselatorManager.GetCachedShape(stack.Collectible is Block ? (stack.Block.ShapeInventory?.Base == null ? stack.Block.Shape.Base : stack.Block.ShapeInventory.Base) : stack.Item.Shape.Base);
				if (stack.Collectible is Block)
				{
					capi.Tesselator.TesselateShape(stack.Collectible, nowTesselatingShape, out meshData, null, null, null);
				}
				else
				{
					capi.Tesselator.TesselateItem(stack.Item, out meshData, this);
				}


			}

			ModelTransform transform = stack.Collectible.Attributes["woodworkingProps"]["idgChoppingBlockProps"]["idgChoppingBlockTransform"].Exists? stack.Collectible.Attributes["woodworkingProps"]["idgChoppingBlockProps"]["idgChoppingBlockTransform"].AsObject<ModelTransform>(): 
			  stack.Collectible.Attributes[this.AttributeTransformCode].AsObject<ModelTransform>();
			transform.EnsureDefaultValues();

			//transform.Rotation.X = 0;
			transform.Rotation.Y = Block.Shape.rotateY;
			//transform.Rotation.Z = 0;
			meshData.ModelTransform(transform);

			return meshData;
		}
		public override void updateMeshes()
        {
			base.updateMeshes();
        }

		private readonly Matrixf mat = new();
	}
}
