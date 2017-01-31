//    This script mines ore from asteroids.
//    before running this script, make sure to prepare as follows:
//    +enter bookmark for mining site and bookmark for station in the configuration section below.
//    +in the Overview create a preset which includes asteroids and rats and enter the name of that preset in the configuration section below at 'OverviewPreset'. The bot will make sure this preset is loaded when it needs to use the overview.
//    +set Overview to sort by distance with the nearest entry at the top.
//    +in the Inventory select the 'List' view.
//    +set the UI language to english.
//    +use a ship with an ore hold.
//    +put some light drones into your ships' drone bay. The bot will make use of them to attack rats when HP are too low (configurable) or it gets jammed.
//    +enable the info panel 'System info'. The bot will use the button in there to access bookmarks and asteroid belts.
//    +arrange windows to not occlude modules or info panels.
//    +in the ship UI, disable "Display Passive Modules" and disable "Display Empty Slots" and enable "Display Module Tooltips". The bot uses the module tooltips to automatically identify the properties of the modules.
//
//    for optional features (such as warp to safe on hostile in local) see the configuration section below.

using BotSharp.ToScript.Extension;
using Parse = Sanderling.Parse;
using static Sanderling.ShipManeuverTypeEnum;

//    begin of configuration section ->

//    The bot uses the bookmarks from the menu which is opened from the button in the 'System info' panel.

//    Bookmarks of places to mine. Add additional bookmarks separated by comma.
string[] SetMiningSiteBookmark = new[] {
    "Mining",
//    "Mining-Alt",
    };

//    Bookmark of location where ore should be unloaded.
string UnloadBookmark = "Station";

//    Name of the container to unload to as shown in inventory.
string UnloadDestContainerName = "Item Hangar";

//    when this is set to true, the bot will try to unload when undocked.
bool UnloadInSpace = false;

//    Bookmark of place to retreat to to prevent ship loss.
string RetreatBookmark = UnloadBookmark;

// Tab to make active before loading preset (if not found, current one will be used)
string OverviewTab = "Mining";

//    The bot loads this preset to the active tab.
string OverviewPreset = "Mining-Spec";
//string OverviewPreset = "Mining-Site";
//string OverviewPreset = "Mining";

var ActivateHardener = true; // activate shield hardener.

//    bot will start fighting (and stop mining) when hitpoints are lower.
var DefenseEnterHitpointThresholdPercent = 65;
var DefenseExitHitpointThresholdPercent = 80;

var EmergencyWarpOutHitpointPercent = 25;
var WarpOutHitpointPercent = 40;

var FightAllRats = true;    //    when this is set to true, the bot will attack rats independent of shield hp.

var EnterOffloadOreHoldFillPercent = 97;    //    percentage of ore hold fill level at which to enter the offload process.

var RetreatOnNeutralOrHostileInLocal = false;   // warp to RetreatBookmark when a neutral or hostile is visible in local.

// never stand still mode if true
// less damage from rats and harder for gankers to kill
var AlwaysMove = true;

// todo: get it from ship's info
var TargettingDistance = 16000;

// asteroid preference model parameters
Dictionary<string, double> asteroidValue = new Dictionary<string, double>();
asteroidValue.Add("Kernite" , 2.29);
asteroidValue.Add("Luminous Kernite" , asteroidValue["Kernite"] * 1.05);
asteroidValue.Add("Fiery Kernite" , asteroidValue["Kernite"] * 1.1);
asteroidValue.Add("Plagioclase" , 2.69);
asteroidValue.Add("Azure Plagioclase" , asteroidValue["Plagioclase"] * 1.05);
asteroidValue.Add("Rich Plagioclase" , asteroidValue["Plagioclase"] * 1.1);
asteroidValue.Add("Jaspet" , 3.0);
asteroidValue.Add("Pure Jaspet" , asteroidValue["Jaspet"] * 1.05);
asteroidValue.Add("Pristine Jaspet" , asteroidValue["Jaspet"] * 1.1);
asteroidValue.Add("Hemorphite" , 2.49);
asteroidValue.Add("Radiant Hemorphite" , asteroidValue["Hemorphite"] * 1.1);
asteroidValue.Add("Vivid Hemorphite" , asteroidValue["Hemorphite"] * 1.05);

double distanceFineWeight = 0.000005;

//    <- end of configuration section

Func<object> BotStopActivity = () => null;

Func<object> NextActivity = MainStep;

int dockWarpCanceled = 0;

//void Approach(Parse.IOverviewEntry overviewEntry) => executeApproach(overviewEntry);
void approach(IUIElement overviewEntry) {
    if (AlwaysMove) {
    	executeOrbit(overviewEntry);
    } else {
    	executeApproach(overviewEntry);
    }
}

for(;;)
{
    MemoryUpdate();

    Host.Log(
        "ore hold fill: " + OreHoldFillPercent + "%" +
        ", mining range: " + MiningRange +
        ", mining modules (inactive): " + SetModuleMiner?.Length + "(" + SetModuleMinerInactive?.Length + ")" +
        ", shield.hp: " + ShieldHpPercent + "%" +
        ", retreat: " + RetreatReason +
        ", JLA: " + JammedLastAge +
        ", overview.rats: " + ListRatOverviewEntry?.Length +
        ", overview.roids: " + ListAsteroidOverviewEntry?.Length +
        ", offload count: " + OffloadCount +
        ", nextAct: " + NextActivity?.Method?.Name);

    CloseModalUIElement();

    if(0 < RetreatReason?.Length && !(Measurement?.IsDocked ?? false))
    {
        // todo: align to warpoff
        while (DronesInSpaceCount > 0 && ShieldHpPercent >= EmergencyWarpOutHitpointPercent) {
            DroneEnsureInBay();
        }
        InitiateDockToOrWarpToBookmark(RetreatBookmark);
        continue;
    }

    try
    {
        NextActivity = NextActivity?.Invoke() as Func<object>;

        if(BotStopActivity == NextActivity)
            break;

        if(null == NextActivity)
            NextActivity = MainStep;
    } catch (Exception e) {
       Host.Log("Exception happened: " + e.ToString());
    }
    Host.Delay(1111);
}

//    seconds since ship was jammed.
long? JammedLastAge => Jammed ? 0 : (Host.GetTimeContinuousMilli() - JammedLastTime) / 1000;

int?    ShieldHpPercent => ShipUi?.HitpointsAndEnergy?.Shield / 10;

bool    DefenseExit =>
    (Measurement?.IsDocked ?? false) ||
    (ListRatOverviewEntry?.Length <= 0) ||
    (DefenseExitHitpointThresholdPercent < ShieldHpPercent && !(JammedLastAge < 40) &&
    !(FightAllRats && 0 < ListRatOverviewEntry?.Length));

bool    DefenseEnter =>
    !DefenseExit    ||
    (ShieldHpPercent < DefenseEnterHitpointThresholdPercent) || JammedLastAge < 10;

bool    OreHoldFilledForOffload => Math.Max(0, Math.Min(100, EnterOffloadOreHoldFillPercent)) <= OreHoldFillPercent;

Int64?    JammedLastTime = null;
string RetreatReasonTemporary = null;
string RetreatReasonPermanent = null;
string RetreatReason => RetreatReasonPermanent ?? RetreatReasonTemporary;
int? LastCheckOreHoldFillPercent = null;

int OffloadCount = 0;

Func<object> MainStep()
{
    if(Measurement?.IsDocked ?? false)
    {
        if (!InInventoryUnloadItems())
            return BotStopActivity;

        if (0 < RetreatReasonPermanent?.Length)
            return BotStopActivity;

        if (0 < RetreatReason?.Length)
            return MainStep;

        Undock();
    }

    if(DefenseEnter)
    {
        Host.Log("enter defense.");
        return DefenseStep;
    }

    EnsureOverviewTypeSelectionLoaded();

    EnsureWindowInventoryOpenOreHold();

    if(ReadyForManeuver)
    {
        DroneEnsureInBay();

        if(OreHoldFilledForOffload)
        {
            if(ReadyForManeuver) {
                InitiateDockToOrWarpToBookmark(UnloadBookmark);
                SetModuleMiner?.EmptyIfNull().Where(
                    miner => miner?.RampActive ?? false).ForEach(
                    activeMiner => ModuleToggle(activeMiner));
            }

            if (UnloadInSpace)
            {
                Host.Delay(4444);
                InInventoryUnloadItems();
            }

            return MainStep;
        }

        if(ListAsteroidOverviewEntry?.Length <= 0)
            InitiateWarpToRandomMiningSite();
    }

    ModuleMeasureAllTooltip();

    if(ActivateHardener)
        ActivateHardenerExecute();

    return InBeltMineStep;
}

int RandomInt() => new Random((int)Host.GetTimeContinuousMilli()).Next();

T RandomElement<T>(IEnumerable<T> sequence)
{
    var array = (sequence as T[]) ?? sequence?.ToArray();

    if (!(0 < array?.Length))
        return default(T);

    return array[RandomInt() % array.Length];
}

void CloseModalUIElement()
{
    var    ButtonClose =
        ModalUIElement?.ButtonText?.FirstOrDefault(button => (button?.Text).RegexMatchSuccessIgnoreCase("close|no|ok"));

    Sanderling.MouseClickLeft(ButtonClose);
}

void DroneLaunch()
{
    Host.Log("launch drones.");
    Sanderling.MouseClickRight(DronesInBayListEntry);
    Sanderling.MouseClickLeft(Menu?.FirstOrDefault()?.EntryFirstMatchingRegexPattern("launch", RegexOptions.IgnoreCase));
}

void DroneEnsureInBay()
{
    if(0 == DronesInSpaceCount)
        return;

    DroneReturnToBay();

    Host.Delay(4444);
}

void DroneReturnToBay()
{
    // click on header in case we're in some text entry where Shift+R won't work as drone command
    var header = WindowDrones?.LabelText?.FirstOrDefault();
    if (header != null) {
        Host.Log("return drones to bay.");
        Sanderling.MouseClickLeft(header);
        Sanderling.KeyboardPressCombined(new []{VirtualKeyCode.SHIFT, VirtualKeyCode.VK_R});
    } else {
        Host.Log("no drones?");
    }
}

Func<object> DefenseStep()
{
    if(DefenseExit || !ReadyForManeuver)
    {
        Host.Log("exit defense.");
        return null;
    }

    if (!(0 < DronesInSpaceCount))
        DroneLaunch();

    EnsureOverviewTypeSelectionLoaded();

    var SetRatName =
        ListRatOverviewEntry?.Select(entry => Regex.Split(entry?.Name ?? "", @"\s+")?.FirstOrDefault())
        ?.Distinct()
        ?.ToArray();

    var SetRatTarget = Measurement?.Target?.Where(target => SetRatName?.Any(
          ratName => target?.TextRow?.Any(row => row.RegexMatchSuccessIgnoreCase(ratName)) ?? false) ?? false);

    var RatTargetNext = SetRatTarget?.Where(target => (target?.DistanceMax ?? int.MaxValue) <= TargettingDistance)
          .OrderBy(target => target?.DistanceMax ?? int.MaxValue)?.FirstOrDefault();

    if(null == RatTargetNext)
    {
        Host.Log("no rat targeted (yet).");
        bool wouldTarget = ListRatOverviewEntry?.EmptyIfNull().Where(
                    rat => (rat.MeTargeted ?? false) || (rat.MeTargeting ?? false)).IsNullOrEmpty() ?? true;
        if (wouldTarget) {
            var rat = ListRatOverviewEntry?.FirstOrDefault();
            executeLock(rat);
        }
    }
    else if (RatTargetNext.Assigned.IsNullOrEmpty())
    {
        Host.Log("rat targeted. sending drones.");
        Sanderling.MouseClickLeft(RatTargetNext);
        Sanderling.KeyboardPress(VirtualKeyCode.VK_F);
    }

    // we need to keep defending here,
    // but it's better to interlieve defense with mining
    return InBeltMineStep;
}

Func<object> InBeltMineStep()
{
    if (!ReadyForManeuver) return null;
    Func<object> nextStep = null;
    if (DefenseEnter)
    {
        Host.Log("enter defense.");
        nextStep = DefenseStep;
    }

    EnsureWindowInventoryOpenOreHold();

    EnsureOverviewTypeSelectionLoaded();

    if(OreHoldFilledForOffload)
        return null;

    var moduleMinerInactive = SetModuleMinerInactive?.FirstOrDefault();

    if (null == moduleMinerInactive)
    {
        if (nextStep == null)
        {
          var delay = ReevaluateTargettedRoids();
          Host.Delay(delay);
        }
        return nextStep;
    }

    var maneuverType = Measurement?.ShipUi?.Indication?.ManeuverType;
    var moving = (maneuverType != null) && (maneuverType.Equals(Approach) || maneuverType == Orbit);

    if (AlwaysMove  && !moving) {
        approach(SetTargetAsteroid?.FirstOrDefault());
    }

    var setTargetAsteroidInRange =
        SetTargetAsteroid?.Where(target => target?.DistanceMax <= MiningRange)?.ToArray();

    var setTargetAsteroidInRangeNotAssigned =
        setTargetAsteroidInRange?.Where(target => !(0 < target?.Assigned?.Length))?.ToArray();

    Host.Log("targeted asteroids in range (without assignment): " + setTargetAsteroidInRange?.Length +
             " (" + setTargetAsteroidInRangeNotAssigned?.Length + ")");

    if(0 < setTargetAsteroidInRangeNotAssigned?.Length)
    {
        var targetAsteroidInputFocus =
            setTargetAsteroidInRangeNotAssigned?.FirstOrDefault(target => target?.IsSelected ?? false);

        if(null == targetAsteroidInputFocus)
            Sanderling.MouseClickLeft(setTargetAsteroidInRangeNotAssigned?.FirstOrDefault());

        ModuleToggle(moduleMinerInactive);
        return nextStep ?? InBeltMineStep;
    }

    IEnumerable<Parse.IOverviewEntry> weightedAsters = ListAsteroidOverviewEntry?.OrderByDescending(aster =>
            asteroidPreferenceWeight(OreTypeFromAsteroidName(aster.Name), 1.0*aster.DistanceMin ?? 0));
    if (SetModuleMinerInactive.Length < SetModuleMiner.Length) // some miners already active
    {
      weightedAsters = RoidsInRange(weightedAsters);
    }
    var asteroidOverviewEntryNext = weightedAsters.FirstOrDefault();
    var asteroidOverviewEntryNextNotTargeted = weightedAsters.FirstOrDefault(entry => !((entry?.MeTargeted ?? false) || (entry?.MeTargeting ?? false)));

    Host.Log("next asteroid: (" + asteroidOverviewEntryNext?.Name + " , distance: " + asteroidOverviewEntryNext?.DistanceMax + ")" +
        ", next asteroid not targeted: (" + asteroidOverviewEntryNextNotTargeted?.Name + " , distance: " + asteroidOverviewEntryNextNotTargeted?.DistanceMax + ")");

    if(null == asteroidOverviewEntryNext)
    {
        Host.Log("no asteroid available");
        return null;
    }

    if(null == asteroidOverviewEntryNextNotTargeted)
    {
        Host.Log("all asteroids targeted");
        ModuleToggle(moduleMinerInactive);
        return nextStep ?? InBeltMineStep;
    }

    if (asteroidOverviewEntryNextNotTargeted.DistanceMax > MiningRange)
    {
        if(1111 > asteroidOverviewEntryNext?.DistanceMin)
        {
            Host.Log("distance to next roid is too large, putting next free miner on already selected one");
            ModuleToggle(moduleMinerInactive);
            return nextStep ?? InBeltMineStep;
        }

        if (!moving) {
            Host.Log("out of range, approaching");
            approach(asteroidOverviewEntryNext);
        }
    } else {
        Host.Log("initiate lock asteroid: " + OreTypeFromAsteroidName(asteroidOverviewEntryNextNotTargeted.Name));
        bool wouldTarget = ListAsteroidOverviewEntry?.EmptyIfNull().Where(
                    t => t.MeTargeting ?? false).IsNullOrEmpty() ?? true;
        if (wouldTarget) executeLock(asteroidOverviewEntryNextNotTargeted);
    }

    return nextStep ?? InBeltMineStep;
}

double asteroidPreferenceWeight(string type, double distance = 0.0)
{
  double typeWeight = 0.1; // default for unknowns
  asteroidValue.TryGetValue(type, out typeWeight);
  var weight = typeWeight - distance * distanceFineWeight;
  //Host.Log("Asteroid weight: " + weight + " / " + type + " / " + distance);
  return weight;
}

void executeApproach(IUIElement overviewEntry) {
    ClickMenuEntryOnMenuRoot(overviewEntry, "approach");
}

void executeOrbit(IUIElement overviewEntry) {
    if (null == overviewEntry) return;
    Host.Log("Orbitting: " + overviewEntry?.Id);
    Sanderling.MouseMove(overviewEntry);
    Sanderling.KeyDown(VirtualKeyCode.VK_W);
    Host.Delay(500);
    Sanderling.MouseClickLeft(overviewEntry);
    Sanderling.KeyUp(VirtualKeyCode.VK_W);
    //ClickThroughMenuPath(overviewEntry, new string[] {"orbit", "500.*"});
}

void executeLock(IUIElement overviewEntry, bool unlock = false) {
    var id = overviewEntry?.Id;
    if (null == id) {
      Host.Log("overviewEntry is null in executeLock");
      return;
    }
    Sanderling.KeyDown(VirtualKeyCode.CONTROL);
    if (unlock) Sanderling.KeyDown(VirtualKeyCode.SHIFT);
    Host.Delay(1000); // lock overview distance
    var toLock = WindowOverview.ListView.Entry.Where(t => t.Id == id).FirstOrDefault();
    Sanderling.MouseClickLeft(overviewEntry);
    if (unlock) Sanderling.KeyUp(VirtualKeyCode.SHIFT);
    Sanderling.KeyUp(VirtualKeyCode.CONTROL);
}

Sanderling.Parse.IMemoryMeasurement    Measurement    =>
    Sanderling?.MemoryMeasurementParsed?.Value;

IWindow ModalUIElement =>
    Measurement?.EnumerateReferencedUIElementTransitive()?.OfType<IWindow>()?.Where(window => window?.isModal ?? false)
    ?.OrderByDescending(window => window?.InTreeIndex ?? int.MinValue)
    ?.FirstOrDefault();

IEnumerable<Parse.IMenu> Menu => Measurement?.Menu;

Parse.IShipUi ShipUi => Measurement?.ShipUi;

bool Jammed => ShipUi?.EWarElement?.Any(EwarElement => (EwarElement?.EWarType).RegexMatchSuccess("electronic")) ?? false;

Sanderling.Parse.IWindowOverview    WindowOverview    =>
    Measurement?.WindowOverview?.FirstOrDefault();

Sanderling.Parse.IWindowInventory    WindowInventory    =>
    Measurement?.WindowInventory?.FirstOrDefault();

IWindowDroneView    WindowDrones    =>
    Measurement?.WindowDroneView?.FirstOrDefault();

ITreeViewEntry InventoryActiveShipOreHold =>
    WindowInventory?.ActiveShipEntry?.TreeEntryFromCargoSpaceType(ShipCargoSpaceTypeEnum.OreHold);

IInventoryCapacityGauge OreHoldCapacityMilli =>
    (InventoryActiveShipOreHold?.IsSelected ?? false) ? WindowInventory?.SelectedRightInventoryCapacityMilli : null;

int? OreHoldFillPercent => (int?)((OreHoldCapacityMilli?.Used * 100) / OreHoldCapacityMilli?.Max);

Tab OverviewPresetTabActive =>
    WindowOverview?.PresetTab
    ?.OrderByDescending(tab => tab?.LabelColorOpacityMilli ?? 0)
    ?.FirstOrDefault();

string OverviewTypeSelectionName =>
    WindowOverview?.Caption?.RegexMatchIfSuccess(@"\(([^\)]*)\)")?.Groups?[1]?.Value;

Parse.IOverviewEntry[] ListRatOverviewEntry => WindowOverview?.ListView?.Entry?.Where(entry =>
        (entry?.MainIconIsRed ?? false)    && (entry?.IsAttackingMe ?? false))
        ?.OrderBy(entry => entry?.DistanceMax ?? int.MaxValue)
        ?.ToArray();

Parse.IOverviewEntry[] ListAsteroidOverviewEntry =>
    WindowOverview?.ListView?.Entry
    ?.Where(entry => null != OreTypeFromAsteroidName(entry?.Name))
    ?.OrderBy(entry => entry.DistanceMax ?? int.MaxValue)
    ?.ToArray();


DroneViewEntryGroup DronesInBayListEntry =>
    WindowDrones?.ListView?.Entry?.OfType<DroneViewEntryGroup>()?.FirstOrDefault(Entry => null != Entry?.Caption?.Text?.RegexMatchIfSuccess(@"Drones in bay", RegexOptions.IgnoreCase));

DroneViewEntryGroup DronesInSpaceListEntry =>
    WindowDrones?.ListView?.Entry?.OfType<DroneViewEntryGroup>()?.FirstOrDefault(Entry => null != Entry?.Caption?.Text?.RegexMatchIfSuccess(@"Drones in Local Space", RegexOptions.IgnoreCase));

int? DronesInSpaceCount => DronesInSpaceListEntry?.Caption?.Text?.AsDroneLabel()?.Status?.TryParseInt();

bool ReadyForManeuverNot =>
    Measurement?.ShipUi?.Indication?.LabelText?.Any(indicationLabel =>
        (indicationLabel?.Text).RegexMatchSuccessIgnoreCase("warp|docking")) ?? false;

bool ReadyForManeuver => !ReadyForManeuverNot && !(Measurement?.IsDocked ?? true);

Sanderling.Parse.IShipUiTarget[] SetTargetAsteroid =>
    Measurement?.Target?.Where(target =>
        target?.TextRow?.Any(textRow => textRow.RegexMatchSuccessIgnoreCase("asteroid")) ?? false)?.ToArray();

Sanderling.Interface.MemoryStruct.IListEntry    WindowInventoryItem    =>
    WindowInventory?.SelectedRightInventory?.ListView?.Entry?.FirstOrDefault();

Sanderling.Accumulation.IShipUiModule[] SetModuleMiner =>
    Sanderling.MemoryMeasurementAccu?.Value?.ShipUiModule?.Where(module => module?.TooltipLast?.Value?.IsMiner ?? false)?.ToArray();

Sanderling.Accumulation.IShipUiModule[] SetModuleMinerInactive     =>
    SetModuleMiner?.Where(module => !(module?.RampActive ?? false))?.ToArray();

int?    MiningRange => SetModuleMiner?.Select(module =>
    module?.TooltipLast?.Value?.RangeOptimal ?? module?.TooltipLast?.Value?.RangeMax ?? module?.TooltipLast?.Value?.RangeWithin ?? 0)?.DefaultIfEmpty(0)?.Min();;

WindowChatChannel chatLocal =>
     Sanderling.MemoryMeasurementParsed?.Value?.WindowChatChannel
     ?.FirstOrDefault(windowChat => windowChat?.Caption?.RegexMatchSuccessIgnoreCase("local") ?? false);

//    assuming that own character is always visible in local
bool hostileOrNeutralsInLocal => 1 != chatLocal?.ParticipantView?.Entry?.Count(IsNeutralOrEnemy);

//    extract the ore type from the name as seen in overview. "Asteroid (Plagioclase)"
string OreTypeFromAsteroidName(string AsteroidName)    =>
    AsteroidName.ValueFromRegexMatchGroupAtIndex(@"Asteroid\s?\(([^\)]+)\s?", 1);

string OreTypeFromAsteroidName(string[] AsteroidName) =>
    OreTypeFromAsteroidName(AsteroidName.Aggregate("", (total, next) => total + next + " "));

void ClickMenuEntryOnMenuRoot(IUIElement MenuRoot, string MenuEntryRegexPattern)
{
    Sanderling.MouseClickRight(MenuRoot);

    var Menu = Measurement?.Menu?.FirstOrDefault();

    var    MenuEntry = Menu?.EntryFirstMatchingRegexPattern(MenuEntryRegexPattern, RegexOptions.IgnoreCase);

    Sanderling.MouseClickLeft(MenuEntry);
}

void ClickThroughMenuPath(IUIElement MenuRoot, string[] MenuEntryRegexPatterns)
{
    if (MenuEntryRegexPatterns.IsNullOrEmpty()) {
    	Host.Log("No menu names given");
    	return;
    }
    Sanderling.MouseClickRight(MenuRoot);
    for (int i = 0; i < MenuEntryRegexPatterns.Length; i++) {
	    var Menu = Measurement?.Menu?.ElementAtOrDefault(i);
	    var    MenuEntry = Menu?.EntryFirstMatchingRegexPattern(MenuEntryRegexPatterns.ElementAtOrDefault(i), RegexOptions.IgnoreCase);
	    Sanderling.MouseClickLeft(MenuEntry);
    }
}

void EnsureWindowInventoryOpen()
{
    if (null != WindowInventory)
        return;

    Host.Log("open Inventory.");
    Sanderling.MouseClickLeft(Measurement?.Neocom?.InventoryButton);
}

bool EnsureWindowInventoryOpenOreHold()
{
    EnsureWindowInventoryOpen();

    var inventoryActiveShip = WindowInventory?.ActiveShipEntry;

    if(InventoryActiveShipOreHold == null && !(inventoryActiveShip?.IsExpanded ?? false))
        Sanderling.MouseClickLeft(inventoryActiveShip?.ExpandToggleButton);
    if(InventoryActiveShipOreHold == null) {
      Host.Log("No ore hold. Not mining ship?");
      Host.Delay(5000);
      return false;
    }

    if(!(InventoryActiveShipOreHold?.IsSelected ?? false))
        Sanderling.MouseClickLeft(InventoryActiveShipOreHold);
    return true;
}

//    sample label text: Intensive Reprocessing Array <color=#66FFFFFF>1,123 m</color>
string InventoryContainerLabelRegexPatternFromContainerName(string containerName) =>
    @"^\s*" + Regex.Escape(containerName) + @"\s*($|\<)";

bool InInventoryUnloadItems() => InInventoryUnloadItemsTo(UnloadDestContainerName);

bool InInventoryUnloadItemsTo(string DestinationContainerName)
{
    Host.Log("unload items to '" + DestinationContainerName + "'.");

    if (!EnsureWindowInventoryOpenOreHold()) {
      return false;
    }

    for (;;)
    {
        var oreHoldListItem = WindowInventory?.SelectedRightInventory?.ListView?.Entry?.ToArray();

        var oreHoldItem = oreHoldListItem?.FirstOrDefault();
        var itemsCount = oreHoldListItem?.Length;
        IUIElement itemToDrag = oreHoldItem;
        if(null == oreHoldItem)
            break;    //    0 items in OreHold

        var DestinationContainerLabelRegexPattern =
            InventoryContainerLabelRegexPatternFromContainerName(DestinationContainerName);
        var DestinationContainer =
            WindowInventory?.LeftTreeListEntry?.SelectMany(entry => new[] { entry }.Concat(entry.EnumerateChildNodeTransitive()))
            ?.FirstOrDefault(entry => entry?.Text?.RegexMatchSuccessIgnoreCase(DestinationContainerLabelRegexPattern) ?? false);
        if (null == DestinationContainer)
            Host.Log("error: Inventory entry labeled '" + DestinationContainerName + "' not found");

        var columnHeader = WindowInventory.SelectedRightInventory.ListView.ColumnHeader?.EmptyIfNull();
				if (!columnHeader.Any()) // we're in icons view
				{
					itemsCount = oreHoldItem.LabelText.Count() / 2; // name and count
					itemToDrag = oreHoldItem.LabelText.First(l => l.Text.Contains("center"));
				}
        if(1 < itemsCount)
            ClickMenuEntryOnMenuRoot(oreHoldItem, @"select\s*all");

        Sanderling.MouseDragAndDrop(itemToDrag, DestinationContainer);
    }
    return true;
}

bool InitiateWarpToRandomMiningSite()    =>
    InitiateDockToOrWarpToBookmark(RandomElement(SetMiningSiteBookmark));

bool InitiateDockToOrWarpToBookmark(string bookmarkOrFolder)
{
    Host.Log("dock to or warp to bookmark or random bookmark in folder: '" + bookmarkOrFolder + "'");

    var listSurroundingsButton = Measurement?.InfoPanelCurrentSystem?.ListSurroundingsButton;

    Sanderling.MouseClickRight(listSurroundingsButton);

    var bookmarkMenuEntry = Measurement?.Menu?.FirstOrDefault()?.EntryFirstMatchingRegexPattern("^" + bookmarkOrFolder + "$", RegexOptions.IgnoreCase);

    if(null == bookmarkMenuEntry)
    {
        Host.Log("menu entry not found for bookmark or folder: '" + bookmarkOrFolder + "'");
        return true;
    }

    var currentLevelMenuEntry = bookmarkMenuEntry;

    for (var menuLevel = 1; ; ++menuLevel)
    {
        Sanderling.MouseClickLeft(currentLevelMenuEntry);

        var menu = Measurement?.Menu?.ElementAtOrDefault(menuLevel);
        var dockMenuEntry = menu?.EntryFirstMatchingRegexPattern("dock", RegexOptions.IgnoreCase);
        var warpMenuEntry = menu?.EntryFirstMatchingRegexPattern(@"warp.*within\s*0", RegexOptions.IgnoreCase);
        var approachEntry = menu?.EntryFirstMatchingRegexPattern(@"approach", RegexOptions.IgnoreCase);

        var maneuverMenuEntry = dockMenuEntry ?? warpMenuEntry;

        if (null != approachEntry)
        {
            if (++dockWarpCanceled <= 5) {
                Host.Log("found menu entry '" + approachEntry.Text + "'. Assuming we are already there.");
                return false;
            }
            Host.Log("Seems we are already there, but haven't completed the maneuver. Retrying");
            dockWarpCanceled = 0;
        }

        if (null != maneuverMenuEntry)
        {
            Host.Log("initiating '" + maneuverMenuEntry.Text + "' on entry '" + currentLevelMenuEntry?.Text + "'");
            Sanderling.MouseClickLeft(maneuverMenuEntry);
            return false;
        }

        var setBookmarkOrFolderMenuEntry =
            menu?.Entry;    //    assume that each entry on the current menu level is a bookmark or a bookmark folder.

        var nextLevelMenuEntry = RandomElement(setBookmarkOrFolderMenuEntry);

        if(null == nextLevelMenuEntry)
        {
            Host.Log("no suitable menu entry found");
            return true;
        }

        currentLevelMenuEntry = nextLevelMenuEntry;
    }
}

void Undock()
{
  while(Measurement?.IsDocked ?? true && (WindowOverview?.ListView?.Entry?.IsNullOrEmpty() ?? true))
  {
  	var undockBtnText = Measurement?.WindowStation?.FirstOrDefault()?.LabelText.FirstOrDefault(
          candidate => candidate.Text.Contains("Undock"))?.Text;
  	if (!(undockBtnText?.Contains("Abort") ?? true)) {
  		Sanderling.MouseClickLeft(Measurement?.WindowStation?.FirstOrDefault()?.UndockButton);
  	}
  	Host.Log("waiting for undocking to complete.");
  	Host.Delay(3333);
  }
  dockWarpCanceled = 0;
  Host.Delay(2222);
  Sanderling.InvalidateMeasurement();
}

void ModuleMeasureAllTooltip()
{
    Host.Log("measure tooltips of all modules.");

    for (;;)
    {
        var NextModule = Sanderling.MemoryMeasurementAccu?.Value?
            .ShipUiModule?.FirstOrDefault(m => null == m?.TooltipLast?.Value);

        if(null == NextModule)
            break;

        Host.Log("measure module.");
        //    take multiple measurements of module tooltip to reduce risk to keep bad read tooltip.
        Sanderling.MouseMove(NextModule);
        Sanderling.WaitForMeasurement();
        Sanderling.MouseMove(NextModule);
    }
}

void ActivateHardenerExecute()
{
    var    SubsetModuleHardener =
        Sanderling.MemoryMeasurementAccu?.Value?.ShipUiModule
        ?.Where(module => module?.TooltipLast?.Value?.IsHardener ?? false);

    var    SubsetModuleToToggle =
        SubsetModuleHardener
        ?.Where(module => !(module?.RampActive ?? false));

    foreach (var Module in SubsetModuleToToggle.EmptyIfNull())
        ModuleToggle(Module);
}

void ModuleToggle(Sanderling.Accumulation.IShipUiModule Module)
{
    var ToggleKey = Module?.TooltipLast?.Value?.ToggleKey;

    Host.Log("toggle module using " +
          (null == ToggleKey ? "mouse" : Module?.TooltipLast?.Value?.ToggleKeyTextLabel?.Text));

    if(null == ToggleKey)
        Sanderling.MouseClickLeft(Module);
    else
        Sanderling.KeyboardPressCombined(ToggleKey);
}

void EnsureOverviewTypeSelectionLoaded()
{
    if(null == OverviewPresetTabActive || null == WindowOverview || null == OverviewPreset)
        return;

    if(!string.Equals(OverviewPresetTabActive.Label.Text, OverviewTab, StringComparison.OrdinalIgnoreCase)) {
        var MiningTab = WindowOverview?.PresetTab.Where(tab => string.Equals(
              tab.Label.Text, OverviewTab, StringComparison.OrdinalIgnoreCase)).FirstOrDefault();
        if (null != MiningTab) {
            Sanderling.MouseClickLeft(MiningTab);
        } else {
            Host.Log("Using active tab as no tab found with name: " + OverviewTab);
        }
    }
    if(string.Equals(OverviewTypeSelectionName, OverviewPreset, StringComparison.OrdinalIgnoreCase))
        return;

    Host.Log("loading preset '" + OverviewPreset + "' to overview (current selection is '" + OverviewTypeSelectionName + "').");
    Sanderling.MouseClickRight(OverviewPresetTabActive);
    Sanderling.MouseClickLeft(Menu?.FirstOrDefault()?.EntryFirstMatchingRegexPattern("load.*preset", RegexOptions.IgnoreCase));
    var PresetMenuEntry = Menu?.ElementAtOrDefault(1)?.EntryFirstMatchingRegexPattern(@"^\s*" + Regex.Escape(OverviewPreset) + @"\s*$", RegexOptions.IgnoreCase);

    if(null == PresetMenuEntry)
    {
        Host.Log("error: menu entry '" + OverviewPreset + "' not found");
        return;
    }

    Sanderling.MouseClickLeft(PresetMenuEntry);
}

void MemoryUpdate()
{
    RetreatUpdate();
    JammedLastTimeUpdate();
    OffloadCountUpdate();
}

void JammedLastTimeUpdate()
{
    if(Jammed)
        JammedLastTime    = Host.GetTimeContinuousMilli();
}

bool MeasurementEmergencyWarpOutEnter =>
    !(Measurement?.IsDocked ?? false) && !(WarpOutHitpointPercent < ShieldHpPercent);

void RetreatUpdate()
{
    RetreatReasonTemporary = (RetreatOnNeutralOrHostileInLocal && hostileOrNeutralsInLocal)    ? "hostile or neutral in local" : null;

    if (!MeasurementEmergencyWarpOutEnter)
        return;

    //    measure multiple times to avoid being scared off by noise from a single measurement.
    Sanderling.InvalidateMeasurement();

    if (!MeasurementEmergencyWarpOutEnter)
        return;

    RetreatReasonTemporary = "shield hp";
    //RetreatReasonPermanent = "shield hp";
}

void OffloadCountUpdate()
{
    var OreHoldFillPercentSynced = OreHoldFillPercent;

    if(!OreHoldFillPercentSynced.HasValue)
        return;

    if(0 == OreHoldFillPercentSynced && OreHoldFillPercentSynced < LastCheckOreHoldFillPercent)
        ++OffloadCount;

    LastCheckOreHoldFillPercent = OreHoldFillPercentSynced;
}

bool IsNeutralOrEnemy(IChatParticipantEntry participantEntry) =>
   !(participantEntry?.FlagIcon?.Any(flagIcon =>
     new[] { "good standing", "excellent standing", "Pilot is in your (fleet|corporation)", }
     .Any(goodStandingText =>
        flagIcon?.HintText?.RegexMatchSuccessIgnoreCase(goodStandingText) ?? false)) ?? false);

        IEnumerable<Parse.IOverviewEntry> RoidsInRange(IEnumerable<Parse.IOverviewEntry> roids = null)
        {
          var initialSet = roids ?? ListAsteroidOverviewEntry?.EmptyIfNull();
          // we usually orbit, so range may fluctuate and we need to be conservative
          return initialSet.Where(aster => aster.DistanceMin < (MiningRange - 2000));
        }

long lastReeval = Host.GetTimeContinuousMilli();
int ReevaluateTargettedRoids()
{
  if (Host.GetTimeContinuousMilli() - lastReeval > 45000)
  {
    lastReeval = Host.GetTimeContinuousMilli();
    var leastValue = SetTargetAsteroid?.EmptyIfNull().Min(
                target => asteroidPreferenceWeight(OreTypeFromAsteroidName(target.TextRow)));

    var mostValueOtherAster = RoidsInRange().Where(roid => !((roid?.MeTargeted ?? false) || (roid?.MeTargeting ?? false))).
          OrderByDescending(roid => asteroidPreferenceWeight(OreTypeFromAsteroidName(roid.Name))).FirstOrDefault();
    var otherVal = asteroidPreferenceWeight(OreTypeFromAsteroidName(mostValueOtherAster.Name));

    Host.Log("Least valued targetted: " + leastValue + "; Most valued available: " + otherVal);

    if (otherVal > leastValue)
    {
      var leastValueRoid = SetTargetAsteroid?.EmptyIfNull().OrderBy(
                  target => asteroidPreferenceWeight(OreTypeFromAsteroidName(target.TextRow))).FirstOrDefault();
      Sanderling.MouseClickLeft(leastValueRoid.Assigned?.FirstOrDefault());
      executeLock(leastValueRoid, true); // unlock
      executeLock(mostValueOtherAster);
      return 2222;
    }
  }
  return 7777;
}
