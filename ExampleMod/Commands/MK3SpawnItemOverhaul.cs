using System;
using System.Text.RegularExpressions;
using Terraria;
using Terraria.ModLoader;
//This code was originally built for the 0.10.1.5 update. Some things may have to be corrected to fit in with the rest of ExampleMod. Please enjoy!
namespace ExampleMod.Commands {
    public class MK3SpawnItemOverhaul : ModCommand
        public override CommandType Type
          =>  CommandType.Chat;
        public override string Command
          =>  "spawnitem";
        public override string Usage
          =>  "/spawnitem [c/008CFF:<Item ID/Name, required> (Stack, default 1, optional)]\n[c/FF7300:Replace spaces in item names with underscores].";
        public override string Description
          =>  "[c/6600FF:MarauderKnight3's Mod \"/spawnitem\" Command] Choose and spawn a quantity of an item. The \"/item\" command that this command was modified from was originally built by the tModLoader team, so credit goes to them where credit is due.";
        //Regex: Detect Non-Number Characters
        private Regex regexDetectNNC = new Regex(@"\D");

        public override void Action(CommandCaller caller, string input, string[] args) {
            if (args.Length >= 1) {
                int type = 0;
                int stack = 1;
                long testLong;
                string testString;
                bool typeConfirmed = false;
                bool stackConfirmed = false;
                var name = args[0].Replace("_", " ");

                if (regexDetectNNC.IsMatch(args[0])) {
                    for (var i = 0; i < ItemLoader.ItemCount; i++) {
                        if (name == Lang.GetItemNameValue(i)) {
                            type = i;
                            typeConfirmed = true;
                            break;
                        }
                    }
                    if (!typeConfirmed) {
                        Main.NewText("Error: Unknown item: \"" + name + "\"", 255, 0, 0);
                    }
                } else {
                    for (var i = 0; i < ItemLoader.ItemCount; i++) {
                        if (args[0] == Convert.ToString(i)) {
                            type = i;
                            typeConfirmed = true;
                            break;
                        }
                    }
                    if (!typeConfirmed) {
                        Main.NewText("Error: Unknown item ID: \"" + args[0] + "\"", 255, 0, 0);
                    }
                }

                if (args.Length >= 2) {
                    if (regexDetectNNC.IsMatch(args[1])) {
                        Main.NewText("Error: Non-number characters found in Stack argument. This includes anything besides 1, 2, 3, 4, 5, 6, 7, 8, 9, and 0.", 255, 0, 0);
                    } else {
                        testString = args[1];
                        while (testString.StartsWith("0")) {
                            testString = testString.Remove(0, 1);
                        }
                        if (testString.Length == 0) {
                            Main.NewText("Info: Stack value was too small. Was 0, now 1", 255, 115, 0);
                            stack = 1;
                            stackConfirmed = true;
                        } else {
                            if (testString.Length > 10) {
                                Main.NewText("Info: Stack value was too big. Was " + testString + ", now 2147483647", 255, 115, 0);
                                stack = 2147483647;
                                stackConfirmed = true;
                            } else {
                                testLong = Convert.ToInt64(testString);
                                if (testLong > 2147483647) {
                                    Main.NewText("Info: Stack value was too big. Was " + testString + ", now 2147483647", 255, 115, 0);
                                    stack = 2147483647;
                                    stackConfirmed = true;
                                } else {
                                    stack = (int)testLong;
                                    stackConfirmed = true;
                                }
                            }
                        }
                    }
                } else {
                    stackConfirmed = true;
                }

                if (args.Length >= 3) {
                    Main.NewText("Error: Argument overflow: " + (args.Length - 2) + " arguments over limit", 255, 0, 0);
                } else if (typeConfirmed && stackConfirmed) {
                    caller.Player.QuickSpawnItem(type, stack);
                    Main.NewText("Spawning " + stack + " of item \"" + Lang.GetItemNameValue(type) + "\"", 55, 255, 85);
                }
            } else {
                Main.NewText("Error: No arguments provided.", 255, 0, 0);
                Main.NewText("Usage:", 0, 140, 255);
                Main.NewText("/spawnitem <Item ID/Name, required> (Stack, default 1, optional)", 0, 140, 255);
                Main.NewText("Replace spaces in item names with underscores.", 255, 115, 0);
            }
        }
    }
}
