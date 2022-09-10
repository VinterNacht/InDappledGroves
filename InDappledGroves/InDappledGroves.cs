﻿using InDappledGroves.BlockEntities;
using InDappledGroves.Blocks;
using InDappledGroves.Util;
using InDappledGroves.Items.Tools;
using Vintagestory.API.Common;
using InDappledGroves.CollectibleBehaviors;
using InDappledGroves.Items;

namespace InDappledGroves
{
    public class InDappledGroves : ModSystem
    {

        public override void Start(ICoreAPI api)
        {
            base.Start(api);

            //Register Items
            api.RegisterItemClass("idgfirewood", typeof(IDGFirewood));
            api.RegisterItemClass("idgplank", typeof(IDGPlank));
            api.RegisterItemClass("idgtool", typeof(IDGTool));
            api.RegisterItemClass("idgbark", typeof(IDGBark));

            //Register Blocks
            api.RegisterBlockClass("idgchoppingblock", typeof(IDGChoppingBlock));
            api.RegisterBlockClass("idgsawbuck", typeof(IDGSawBuck));
            api.RegisterBlockClass("idgsawhorse", typeof(IDGSawHorse));
            api.RegisterBlockClass("idgbarkbasket", typeof(BarkBasket));
            api.RegisterBlockClass("idgboardblock", typeof(IDGBoardBlock));

            //Register BlockEntities
            api.RegisterBlockEntityClass("idgbechoppingblock", typeof(IDGBEChoppingBlock));
            api.RegisterBlockEntityClass("idgbesawbuck", typeof(IDGBESawBuck));
            api.RegisterBlockEntityClass("idgbesawhorse", typeof(IDGBESawHorse));


            //Register CollectibleBehaviors
            api.RegisterCollectibleBehaviorClass("woodsplitter", typeof(BehaviorWoodChopping));
            api.RegisterCollectibleBehaviorClass("woodsawer", typeof(BehaviorWoodSawing));
            api.RegisterCollectibleBehaviorClass("woodplaner", typeof(BehaviorWoodPlaning));
            api.RegisterCollectibleBehaviorClass("woodhewer", typeof(BehaviorWoodHewing));



            //Check for Existing Config file, create one if none exists
            try
            {
                var Config = api.LoadModConfig<InDappledGrovesConfig>("indappledgroves.json");
                if (Config != null)
                {
                    api.Logger.Notification("Mod Config successfully loaded.");
                    InDappledGrovesConfig.Current = Config;
                }
                else
                {
                    api.Logger.Notification("No Mod Config specified. Falling back to default settings");
                    InDappledGrovesConfig.Current = InDappledGrovesConfig.GetDefault();
                }
            }
            catch
            {
                InDappledGrovesConfig.Current = InDappledGrovesConfig.GetDefault();
                api.Logger.Error("Failed to load custom mod configuration. Falling back to default settings!");
            }
            finally
            {
                api.StoreModConfig(InDappledGrovesConfig.Current, "indappledgroves.json");
            }
        }
    }
}
