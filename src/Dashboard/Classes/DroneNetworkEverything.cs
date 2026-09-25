namespace RRSOS.PCC.Dashboard
{
    public static partial class DroneNetworkEngine
    {
        /// <summary>
        /// What the game's "Supply everything" writes, in its own order, as of the owner's Custom-1 save. Used only when the
        /// save being edited has no drone settings to learn the list from; otherwise the save's own lists win, since the
        /// set grows with game versions (Custom-1 has 211 entries, the older Custom-2 has 205).
        /// </summary>
        private static readonly string[] FallbackEverything =
        {
            "OxygenCapsule1", "WaterBottle1", "ice", "Cobalt", "Iridium", "Iron", "Magnesium", "Silicon",
            "Titanium", "Aluminium", "Uranim", "Alloy", "Osmium", "Sulfur", "Zeolite", "Obsidian",
            "Bauxite", "Dolomite", "Uraninite", "Selenium", "Phosphorus", "Amber", "PulsarQuartz", "BalzarQuartz",
            "MagnetarQuartz", "QuasarQuartz", "SolarQuartz", "CosmicQuartz", "Seed0", "Seed1", "Seed2", "Seed3",
            "Seed4", "Seed5", "Seed6", "SeedGold", "Seed7Humble", "Seed8Humble", "Seed9Humble", "Seed10Humble",
            "Seed11Humble", "FabricBlue", "SmartFabric", "Rod-uranium", "Rod-iridium", "Rod-alloy", "Rod-osmium", "astrofood",
            "astrofood2", "RocketReactor", "RocketReactor2", "Vegetable0Seed", "Vegetable0Growable", "Vegetable1Seed", "Vegetable1Growable", "Vegetable2Seed",
            "Vegetable2Growable", "Vegetable3Seed", "Vegetable3Growable", "CookCocoaSeed", "CookCocoaGrowable", "CookWheatSeed", "CookWheatGrowable", "CookStew1",
            "CookStewFish1", "honey", "Algae1Seed", "Bacteria1", "Bioplastic1", "FusionEnergyCell", "Fertilizer1", "Fertilizer2",
            "Fertilizer3", "RedPowder1", "MethanCapsule1", "NitrogenCapsule1", "Mutagen1", "Mutagen2", "Mutagen3", "Mutagen4",
            "TreeRoot", "Tree0Seed", "Tree1Seed", "Tree2Seed", "Tree3Seed", "Tree4Seed", "Tree5Seed", "Tree6Seed",
            "Tree7Seed", "Tree8Seed", "Tree9Seed", "Tree10Seed", "Tree11Seed", "Tree12Seed", "Tree13Seed", "Tree14Seed",
            "Tree15Seed", "Tree16Seed", "Tree17Seed", "Tree18Seed", "LarvaeBase1", "Bee1Larvae", "Butterfly1Larvae", "Butterfly2Larvae",
            "Butterfly3Larvae", "Butterfly4Larvae", "Butterfly6Larvae", "Butterfly7Larvae", "Butterfly8Larvae", "Butterfly9Larvae", "Butterfly5Larvae", "Butterfly10Larvae",
            "LarvaeBase2", "SilkWorm", "LarvaeBase3", "Butterfly11Larvae", "Butterfly12Larvae", "Butterfly13Larvae", "Butterfly14Larvae", "Butterfly15Larvae",
            "Butterfly16Larvae", "Butterfly17Larvae", "Butterfly18Larvae", "Butterfly19Larvae", "Butterfly20Larvae", "Butterfly21Larvae", "Silk", "Phytoplankton1",
            "Fish2Eggs", "Fish3Eggs", "Fish4Eggs", "Fish5Eggs", "Fish6Eggs", "Fish7Eggs", "Fish8Eggs", "Fish1Eggs",
            "Fish9Eggs", "Fish10Eggs", "Fish11Eggs", "Fish12Eggs", "Fish13Eggs", "Fish14Eggs", "Fish15Eggs", "CircuitBoard1",
            "Drone1", "Drone2", "Drone3", "CookChocolate", "CookFlour", "CookCroissant", "CookCookie1", "CookCake1",
            "Frog1Eggs", "FrogGoldEggs", "Frog2Eggs", "Frog10Eggs", "Frog11Eggs", "Frog9Eggs", "Frog8Eggs", "Frog7Eggs",
            "Frog6Eggs", "Frog5Eggs", "Frog4Eggs", "Frog3Eggs", "Frog13Eggs", "Frog12Eggs", "Frog15Eggs", "Frog14Eggs",
            "Frog16Eggs", "FuseCartridge", "FusePressure1", "FuseHeat1", "FuseEnergy1", "FusePlants1", "FuseOxygen1", "FuseProduction1",
            "FuseTradeRocketsSpeed1", "FuseAnimals1", "FuseGrowth1", "FuseInsects1", "FusePurification1", "Keycard1", "KeyCard2", "Flare",
            "Explosive", "GeneticTrait", "DNASequence", "AnimalFood1", "AnimalFood2", "AnimalFood3", "ToxicGoo", "ToxicWater",
            "PurifiedWater", "PurificationCapsule", "PurificationGel", "Toxins", "ToxicSpores", "MicroPlastics", "ChlorineCapsule1", "PristineMushroom",
            "PlasticPolymer", "Rod-plastic", "Minable-Tungsten", "Rod-tungsten", "AntiToxinsExplosive1", "AntiToxinsExplosive2", "ToxicityMedecine", "ToxicityAmmo",
            "ToxicityAmmoPack", "ToxicityMedecinePack", "AnimalBones",
        };
    }
}
