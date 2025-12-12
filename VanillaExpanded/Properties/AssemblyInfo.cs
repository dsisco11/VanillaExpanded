using Vintagestory.API.Common;

[assembly: ModInfo("VanillaExpanded", "vanillaexpanded",
    CoreMod = true,
    WorldConfig = """
    {
        "playstyles": [],
        "worldConfigAttributes": [
            { 
                "category": "spawnndeath", 
                "code": "veDeathPenalty", 
                "dataType": "dropdown", 
                "values": ["vanilla", "bag", "unequipped", "all"], 
                "names": ["Vanilla (use game setting)", "Lose bag items only", "Lose unequipped items", "Lose all items"], 
                "default": "vanilla" 
            },
            {
                "category": "spawnndeath", 
                "code": "veToolDuraLoss", 
                "dataType": "dropdown", 
                "values": ["0", "5", "10", "15", "20", "25", "50"], 
                "names": ["None", "5%", "10%", "15%", "20%", "25%", "50%"], 
                "default": "0" 
            }
        ]
    }
    """
)]

[assembly: ModDependency("game")]