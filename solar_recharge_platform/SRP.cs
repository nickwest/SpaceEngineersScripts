/*
    Solar Recharge Platform (SRP)

Copyright 2015 Nick West
Released under CC0 1.0 Universal (Public Domain)
 ^ This licenses lets you do pretty much whatever you want with the below code without credit or anything

Purpose:
    Automated alignment of Solar Recharge Platform (SRP for short, which is basically any ship with solar panels) with sun for optimal power production.
    Also includes battery management with automated charging of batteries that are docked to the SRP.
    Works on both large and small ships, can be used on satelites/antenna relays/probes/etc.

Solar Alignment
    Ship self aligns to point solar panels toward the sun
    Averages the power across multiple solar panels when aligning for optimization
    Uses all Gyros on the ship to make the alignment (Requires Gyros set to "overide" for auto-alignment to work)
    Adjustable minimum value for power (Large solar panel max of 120 kW, default set to 118 kW, small solar panel max out of 22 kW, default is set to 20.5).
    Adjustable turn speed (higher numbers mean faster turns but also lower accuracy for alignment, if speed is high keep min val lower).

Battery management:
    Batteries that are attached to the SRP will be used to suppliment solar power when charging docked Batteries for maximum charging speed
    Any battery on a ship that docks with the SRP will be set to recharge or off upon docking (optional with config below)
    All docked batteries will fully charge and have charging priority over SRP batteries
    When all docked batteries are charged, or there is extra solar power, SRP batteries will be set to recharge automatically (but only if the solar array is optimal)
    Battery management can be turned off with a config option below


Required for alignment:
    1 or more Gyroscopes (TODO??if multiple gyros they all have to be facing the same way??)
    1 or more Solar Panels
    1 Programmable Block
    1 Timer Block
        * Set for 2 seconds (you can play with different interval speeds, 2 works well though)
        * Have it run the Programmable Block and start its own timer. (to make the program loop)

Optional:
    1 or more batteries on the station
    1 or more batteries docked to the station (via a connector, piston, or rotor)
        * Merged blocks show up as being owned by the main grid, whereas docked blocks show as being seperate
        * Blocks attached via landing gear only will not show on the grid and cannot have their batteries charged

Instructions for use:
    1. You must own all parts (solar panels, gyros, batteries, programmable block, timer, etc).
    2. Install the script onto your programmable block
    3. Customize the Custom Variables below (read notes next to them for more info)
    4. Set the following actions to the timer block
        a. Run programmable block
        b. start timer on this timer block
    5. (optional, but recommended) Position your array so it's pointed toward the sun rather than away it
       depending on ship config, it could get stuck faced away from the sun if it doesn't start facing it
    6. Set all gyros to "overide" (unless you changed automaticGyroControl to true, then this step is not necessary)
    7. Bask in the glory as your Solar Recharge Platform moves to face the sun, and your power generation reaches peak values and sustains them.

Notes:
    I recommend leaving the autoGyroOverride set to false and then manually toggling your gyros from override to not.
        This leaves you in control of when the ship will be controllable and not. Otherwise you have to stop the timer to take contorl of the ship
    All solar panels should point the same way OR adjust the minPwr variable lower to compensate since it's impossible to get all panels at 98% if they can't all face the sun at once
    No custom names for blocks are needed, and this script will not rename any of your blocks :)
    If you're not running the game in English, locate the regex strings (not far below) and adjust them to match your language (I'll look for a better way to do this)

Credit:
    This script was inspired by Kir's "Self-Aligning Solar Panels For Ships With Gyros" script
    Found here: http://steamcommunity.com/sharedfiles/filedetails/?id=369286464

    Thanks to Me 10 Jin's regex example for extracting detailed data from solar panels and batteries
    Found here: http://forum.keenswh.com/threads/regular-expressions-example-reactor-usage.7226038/
*/

//***********************************************************************************************************************************
//**************************************************** Custom Variables *************************************************************
//***********************************************************************************************************************************

// TODO: Auto detect if possible
const string shipType               = "large";  // Can be "large" or "small", used to calculate solar power efficiency (small panels have a different max output)
const double minPwrPercent   = 98;        // Minimum power percentage [Note: going over 100% will make the SRP constantly seek the sun. Similarly any percent close to 100 could result in constant seeking if percentage is not achievable with Solar Panel Configuration]
const float turnSpeed               = 2.5f;      // Turn speed of craft, slower means more precision, you can adjust timer block to faster tick if you set this to be faster

/********************************
 * INFO ABOUT SELECTING CONTROLS:
 *   - For optimal alignment speed you don't want your solar panels to spin flat
 *      For bottom/top facing solar panels - Roll + Pitch should be used
 *      For front/back facing solar panels - Pitch + Yaw should be used
 *      Fot right/left facing solar panels - Roll + Yaw should be used
 ********************************/
const string control1    = "Roll";        // First control to use on the Gyro to position
const string control2   = "Pitch";      // Second control to use on the Gyro to position

const bool autoGyroOverride = false;    // When set to true, the script will turn all gyros to override automatically while aligning to the sun
                                        // It will also then release the override when alignment is optimal (restoring pilot control of gyros)
                                        // The alternate to this is to manually control the overide (leave it on, except when you want to "drive") <- I prefer this way

const bool batteryManagement        = true;    // If this is set to false, battery management won't happen, but Gyro positioning still will
const bool chargeDockedBatteries  = true;    // If batteryManagement is set to false, this does nothing. Otherwise it allows this script to manage docked batteries as well (useful for SRPs)
const bool keepBatteriesOn             = true;    // Keep batteries on and discharging except when solar is full power [Note: recommended to prevent complete power failures]

//***********************************************************************************************************************************
//********************************************** Shouldn't need to edit below here but might ****************************************
//***********************************************************************************************************************************

//--  Constants & Debug flags

// Don't change these unless a game update changes them (Watts) //TODO: Verify these values and/or get them automatically
const int smallBatteryMax         = 4320000;
const int largeBatteryMax          = 12000000;
const int smallSolarPanelMax   = 30000;
const int largeSolarPanelMax    = 120000;

// Flipping these to true will Echo out various debug info (I didn't bother to pull this out, but you'll have to look at code to see what it's echoing exactly)
bool debugMain          = false;
bool debugAlignment = false;
bool debugBat            = false;

//--  These probably need to be adjusted based on localization (just change the English words to whatever matches the language you're using)

// TODO: Fix localization issues with these Regex patterns. Instead, assume positions based on line
// Example: someBlock.DetailedInfo.Split(new string[] { "\r\n", "\n", "\r" }, StringSplitOptions.None);

// regex to get max and current output from solar panel details
System.Text.RegularExpressions.Regex solarPwrRegex = new System.Text.RegularExpressions.Regex(
    "Max Output: (\\d+\\.?\\d*) (\\w?)W.*Current Output: (\\d+\\.?\\d*) (\\w?)W",
    System.Text.RegularExpressions.RegexOptions.Singleline
);

// regex to get power data from battery details.
System.Text.RegularExpressions.Regex batteryRegex = new System.Text.RegularExpressions.Regex(
    "Max Output: (\\d+\\.?\\d*) (\\w?)W.*Max Required Input: (\\d+\\.?\\d*) (\\w?)W.*Max Stored Power: (\\d+\\.?\\d*) (\\w?)Wh.*Current Input: (\\d+\\.?\\d*) (\\w?)W.*Current Output: (\\d+\\.?\\d*) (\\w?)W.*Stored power: (\\d+\\.?\\d*) (\\w?)Wh",
    System.Text.RegularExpressions.RegexOptions.Singleline
);

// Global variables until this all becomes a class
List<IMyTerminalBlock> srpSolarBlocks = null;       // Local grid only
List<IMyTerminalBlock> srpGyroBlocks = null;        // Local grid only
List<IMyTerminalBlock> srpBatteryBlocks = null;     // Local grid only
List<IMyTerminalBlock> dockedBatteryBlocks = null;  // Non-Local grid only

IMyProgrammableBlock thisProgrammableBlock = null;  // This programmable block (used for identifying local grid)

double currentPwr;       // Stores the current power generated by the solar panel
double lastPwr;          // Stores the power generated by the solar panel at the previous alignment
double highestPwr = 0;   // Stores highest power value
double minPwr;           // Minimum acceptable power (based on percentage & ship type defined above)

/**
 *	Entry point of script
 *
 * 	@return void
 */
void Main()
{
    // Get the ProgrammableBlock this is running on
    thisProgrammableBlock = getSelf();

    // Instantiate Lists
    srpSolarBlocks      = new List<IMyTerminalBlock>();
    srpGyroBlocks       = new List<IMyTerminalBlock>();
    srpBatteryBlocks    = new List<IMyTerminalBlock>();
    dockedBatteryBlocks = new List<IMyTerminalBlock>();

    // Populate Lists
    GridTerminalSystem.GetBlocksOfType<IMySolarPanel>(srpSolarBlocks, filterSRPSolarPanels);        // Find all the SRP solar panels
    GridTerminalSystem.GetBlocksOfType<IMyGyro>(srpGyroBlocks, filterSRPGyros);                     // Find all the SRP gyros
    GridTerminalSystem.GetBlocksOfType<IMyBatteryBlock>(srpBatteryBlocks, filterSRPBatteries);      // Find all the SRP batteries
    GridTerminalSystem.GetBlocksOfType<IMyBatteryBlock>(dockedBatteryBlocks, filterDockedBatteries);// Find all the docked batteries

    // If there aren't Gyros, we can't position, so throw an exception
    if(srpGyroBlocks.Count <= 0){
         throw new System.ArgumentException("No Gyros Found, Cannot Position!", "CAT");
    }

    // Calculate the minimum solar panel output we'll accept
    minPwr = minPwrPercent / 100 * (shipType == "large" ? largeSolarPanelMax : smallSolarPanelMax) / 1000; // KW

    // Check if Solar panels exist (if not, we can't align them)
    if (srpSolarBlocks.Count > 0) {
        // Get current average power across all solar panels
        double totalPanelPwr = 0;
        double countPanels   = 0;

        for (int i = 0; i < srpSolarBlocks.Count ; i++ ){
            countPanels++;

            System.Text.RegularExpressions.Match match = solarPwrRegex.Match(srpSolarBlocks[i].DetailedInfo);
            double n;
            if(match.Success ){
                // Max output of the solar panel
                if(double.TryParse( match.Groups[1].Value, out n )){
                    totalPanelPwr += n * Math.Pow( 1000.0, ".kMGTPEZY".IndexOf( match.Groups[2].Value));
                }
            }
        }

        // Set the current average power generated by the panels
        currentPwr = totalPanelPwr / countPanels / 1000; // KW

        myDebug("Min="+minPwr.ToString(), debugAlignment);
        myDebug("Curr="+currentPwr.ToString(), debugMain);

        // Align the solar array
        AlignSolar();

    }else{
        throw new System.ArgumentException("No Solar Panels Found", "CAT");
    }

    // Manage the batteries if that option is seleted
    if(batteryManagement){
        manageBatteries();
    }

}

/**
 *	Align the solar array using gyros
 *  TODO: Make it so this can flip 180 when it's stuck with the wrong side facing the sun
 *
 * 	@return void
 */
void AlignSolar(){
    // Set the GyroState variables
    setGyroState(control1);
    setGyroState(control2);

    // Get the last and highest power from storage
    lastPwr = getVar("currentPower");
    highestPwr = getVar("highestPower");

    myDebug("Last="+lastPwr.ToString(), debugAlignment);
    myDebug("High="+highestPwr.ToString(), debugAlignment);

    // Make sure only one Gyro control is set to move and that all gyros are functioning the same way
    if(!verifyGyros()){
        myDebug("Reset Gyros", debugAlignment);
        // Stop moving
        zeroGyros();

        // Clear last and highest vars
        saveVars(0,0);

        return;
    }

    // If we hit optimal power then just stay put.
    if(currentPwr >= minPwr){
        myDebug("SUCCESS", debugAlignment);

        // Stop moving
        zeroGyros();

        // Clear last and highest vars
        saveVars(0,0);

        // Return Gyro control (if we're doing that sort of thing)
        if(autoGyroOverride){
            overrideGyros(false);
        }

        return;
    }else{
        // Always set the currentPower to Storage (this will be lastPwr next tick)
        saveVar("currentPower", currentPwr);

        // Take control of Gyros (if we're doing that)
        if(autoGyroOverride){
            overrideGyros(true);
        }

        // Not moving, power is not optimal
        if(getGyroState(control1) == 0 & getGyroState(control2) == 0){
            myDebug("Just Starting (0)", debugAlignment);

            // Zero out highest Power
            saveVar("highestPower", 0);

            // Make sure the gyros reflect the known state
            zeroGyros();

            // Start moving in the first direction
            GyroRotation(control1, 1);

            return;
        }
        // Control 1 was active
        else if(getGyroState(control1) != 0){
            if(highestPwr == 0 & lastPwr == 0){ // This condition shouldn't really ever be met
                myDebug("Start Going (1)", debugAlignment);

                return;
            }
            else if(highestPwr == 0 & currentPwr < lastPwr){
                myDebug("Reverse direction (1)", debugAlignment);

                GyroRotation(control1, -1 * getGyroState(control1));

                return;
            }
            else if(currentPwr >= lastPwr){
                myDebug("Keep Going (1)", debugAlignment);

                // Save highest power
                highestPwr = currentPwr;
                saveVar("highestPower", highestPwr);

                return;
            }
            else{
                myDebug("Switch to (2)", debugAlignment);

                GyroRotation(control1, 0);
                GyroRotation(control2, 1);

                // Clear last and highest vars
                saveVars(0,0);

                return;
            }
        }
        // Control 2 was active
        else if(getGyroState(control2) != 0){
            if(highestPwr == 0 & lastPwr == 0){
                myDebug("Start Going (2)", debugAlignment);

                return;
            }
            else if(highestPwr == 0 & currentPwr < lastPwr){
                myDebug("Reverse direction (2)", debugAlignment);

                GyroRotation(control2, -1 * getGyroState(control2));

                return;
            }
            else if(currentPwr >= lastPwr){
                myDebug("Keep Going (2)", debugAlignment);

                // Save highest power
                highestPwr = currentPwr;
                saveVar("highestPower", highestPwr);

                return;
            }
            else{
                myDebug("Switch to (1)", debugAlignment);

                GyroRotation(control2, 0);
                GyroRotation(control1, 1);

                // Clear last and highest vars
                saveVars(0,0);

                return;
            }

        }
    }
}

/**
 *	Set the gyro state based on the values in the first gyro found in gyroBlocks
 *
 *  control = ["Yaw" | "Pitch | "Roll"]
 *
 *  @param string control
 * 	@return void
 */
void setGyroState(string control){
    // Pick one of the gyros to look at
    IMyGyro gyroBlock = srpGyroBlocks[0] as IMyGyro;

    switch(control){
        case "Yaw":
            yaw_state = gyroBlock.Yaw == 0 ? 0 : gyroBlock.Yaw > 0 ? 1 : -1;
        break;
        case "Roll":
            roll_state = gyroBlock.Roll == 0 ? 0 : gyroBlock.Roll > 0 ? 1 : -1;
        break;
        case "Pitch":
            pitch_state = gyroBlock.Pitch == 0 ? 0 : gyroBlock.Pitch > 0 ? 1 : -1;
        break;
    }
}

/**
 *	Set all the gyro controls to 0 so we start fresh
 *
 * 	@return void
 */
void zeroGyros(){
    GyroRotation("Yaw", 0);
    GyroRotation("Roll", 0);
    GyroRotation("Pitch", 0);
}

/**
 *	Check all gyros, if there are conflicting controls set, or more than 1 return false
 *
 * 	@return bool
 */
bool verifyGyros(){
    IMyGyro tmpGyro;

    string type = "";
    float val = 0;
    for ( int i = 0; i < srpGyroBlocks.Count ; i++ ){
        tmpGyro = srpGyroBlocks[i] as IMyGyro;
        if(tmpGyro.Yaw != 0){
            if(type != "" & type != "Yaw"){
                return false;
            }else{
                if(val != 0 & tmpGyro.Yaw != val){
                    return false;
                }
                type = "Yaw";
                val = tmpGyro.Yaw;
            }
        }
        if(tmpGyro.Pitch != 0){
            if(type != "" & type != "Pitch"){
                return false;
            }else{
                if(val != 0 & tmpGyro.Pitch != val){
                    return false;
                }
                type = "Pitch";
                val = tmpGyro.Pitch;
            }
        }
        if(tmpGyro.Roll != 0){
            if(type != "" & type != "Roll"){
                return false;
            }else{
                if(val != 0 & tmpGyro.Roll != val){
                    return false;
                }
                type = "Roll";
                val = tmpGyro.Roll;
            }
        }
    }

    return true;
}

/**
 *	Set override on all gyros
 *
 *  @param bool setOverride
 * 	@return void
 */
void overrideGyros(bool setOverride){
    IMyGyro tmpGyro;

    for ( int i = 0; i < srpGyroBlocks.Count ; i++ ){
        tmpGyro = srpGyroBlocks[i] as IMyGyro;
        if((!tmpGyro.GyroOverride & setOverride) | (tmpGyro.GyroOverride & !setOverride)){
            tmpGyro.GetActionWithName("Override").Apply(tmpGyro);
        }
    }
}

/**
 *	Get the gyro state based on the state variables
 *
 *  control = ["Yaw" | "Pitch | "Roll"]
 *
 *  @param string control
 * 	@return int
 */
int getGyroState(string control){
    switch(control){
        case "Yaw":
            return yaw_state;
        break;
        case "Roll":
            return roll_state;
        break;
        case "Pitch":
            return pitch_state;
        break;
        default:
            throw new System.ArgumentException("Invalid control in config (must be proper case)", "CAT");
        break;
    }
    return 0;
}

// More globals because this isn't encapsulated
int yaw_state = 0;
int pitch_state = 0;
int roll_state = 0;

/**
 *	Set all gyros to on the given rotation control (rotation) at the given speed (val)
 *
 *  rotation = ["Yaw" | "Pitch | "Roll"]
 *  val = negative or positive int
 *
 *  @param string rotation
 *  @param int val
 * 	@return int
 */
void GyroRotation(string rotation, int val){
    switch(rotation){
        case "Pitch":
            pitch_state = val;
        break;
        case "Yaw":
            yaw_state = val;
        break;
        case "Roll":
            roll_state = val;
        break;
    }

    float speed = 0.01f * turnSpeed * val;
    for (int i = 0; i < srpGyroBlocks.Count ; i++ ){
        (srpGyroBlocks[i] as IMyGyro).SetValueFloat(rotation, speed);
    }
}

// Temp global variables
double temp_output_max      = 0.0;
double temp_output_cur      = 0.0;
double temp_input_max       = 0.0;
double temp_input_cur       = 0.0;
double temp_storage_max     = 0.0;
double temp_storage_cur     = 0.0;

/**
 *	Set battery variables from the Details tring to the temp vars
 *
 *  tempBlock = any powered IMyTerminalBlock
 *
 *  @param IMyTerminalBlock tempBlock
 * 	@return bool
 */
bool setTempBatteryData(IMyTerminalBlock tempBlock){
    // If it's not a battery, get out of here
    if(!(tempBlock is IMyBatteryBlock)){
        return false;
    }

    // Get all the juicy info about the battery (yes this comment is a play on the word juicy)
    System.Text.RegularExpressions.Match match = batteryRegex.Match(tempBlock.DetailedInfo);
    if(match.Success){
        Double n;
        if(Double.TryParse(match.Groups[1].Value, out n)){ // max output
            temp_output_max = n * Math.Pow( 1000.0, ".kMGTPEZY".IndexOf(match.Groups[2].Value));
        }
        if(Double.TryParse(match.Groups[3].Value, out n)){ // max input
            temp_input_max = n * Math.Pow( 1000.0, ".kMGTPEZY".IndexOf(match.Groups[4].Value));
        }
        if(Double.TryParse(match.Groups[5].Value, out n)){ // max output
            temp_storage_max = n * Math.Pow( 1000.0, ".kMGTPEZY".IndexOf(match.Groups[6].Value));
        }
        if(Double.TryParse(match.Groups[7].Value, out n)){ // current input
            temp_input_cur = n * Math.Pow( 1000.0, ".kMGTPEZY".IndexOf(match.Groups[8].Value));
        }
        if(Double.TryParse(match.Groups[9].Value, out n)){ // current output
            temp_output_cur = n * Math.Pow( 1000.0, ".kMGTPEZY".IndexOf(match.Groups[10].Value));
        }
        if(Double.TryParse(match.Groups[11].Value, out n)){ // current output
            temp_storage_cur = n * Math.Pow( 1000.0, ".kMGTPEZY".IndexOf(match.Groups[12].Value));
        }
        return true;
    }
    return false;
}

/**
 *	Run the battery management routine
 *
 * 	@return void
 */
void manageBatteries(){
    if(srpSolarBlocks.Count > 0 & (srpBatteryBlocks.Count > 0 | dockedBatteryBlocks.Count > 0)){
        // Solar array output info
        double solarr_output_cur    = 0.0;
        double solarr_output_max    = 0.0;
        double solarr_output_avail  = 0.0;

        // Solar Batteries output info
        double solbat_output_cur        = 0.0;
        double solbat_output_max        = 0.0;
        double solbat_output_avail      = 0.0;
        double solbat_output_avail_idle = 0.0;

        // Solar Batteries input info
        double solbat_input_cur     = 0.0;
        double solbat_input_max     = 0.0;
        double solbat_input_needed  = 0.0;

        // Ship Batteries input info
        double shipbat_input_cur    = 0.0;
        double shipbat_input_max    = 0.0;
        double shipbat_input_needed = 0.0;

        // Flags
        bool we_done = false;
        bool ship_needs_charge = false;
        bool solbat_needs_charge = false;

        // Math variable
        double pwrDeficit = 0;

        // Get Solar Array values
        for(int i = 0; i < srpSolarBlocks.Count ; i++){
            System.Text.RegularExpressions.Match match = solarPwrRegex.Match(srpSolarBlocks[i].DetailedInfo);
            Double n;
            if(match.Success ){
                // Max output of the solar panel
                if(Double.TryParse( match.Groups[1].Value, out n )){
                    solarr_output_max += n * Math.Pow( 1000.0, ".kMGTPEZY".IndexOf( match.Groups[2].Value));
                }
                // Current output of the solar panel
                if(Double.TryParse( match.Groups[3].Value, out n )){
                    solarr_output_cur += n * Math.Pow( 1000.0, ".kMGTPEZY".IndexOf( match.Groups[4].Value));
                }
            }
        }
        solarr_output_avail = solarr_output_max - solarr_output_cur; // Available power

        // Get docked battery info
        for(int i = 0; i < dockedBatteryBlocks.Count; i++){
            // Get the detailed power data and put it in the temp vars
            if(setTempBatteryData(dockedBatteryBlocks[i])){
                // Ship batteries are always set to recharge
                if(!dockedBatteryBlocks[i].DetailedInfo.Contains("recharged")){
                    dockedBatteryBlocks[i].GetActionWithName("Recharge").Apply(dockedBatteryBlocks[i]);
                    // Turn it off so we don't overload the array by adding a lot of new batteries at once.
                    if((dockedBatteryBlocks[i] as IMyFunctionalBlock).Enabled){
                        dockedBatteryBlocks[i].GetActionWithName("OnOff_Off").Apply(dockedBatteryBlocks[i]);
                    }
                }

                // If it's on and set to recharge, and it's not full, and it's functional, include it
                if((dockedBatteryBlocks[i] as IMyFunctionalBlock).Enabled & dockedBatteryBlocks[i].DetailedInfo.Contains("recharged") & temp_storage_max > temp_storage_cur){
                    shipbat_input_max += temp_input_max;
                    shipbat_input_cur += temp_input_cur;
                }
                // If it's not full and not set to recharge, make note that we have batteries still needing charge
                else if(temp_storage_max > temp_storage_cur & !(dockedBatteryBlocks[i] as IMyFunctionalBlock).Enabled){
                    ship_needs_charge = true;
                }
            }
        }

        // Get SRP Battery info
        for(int i = 0; i < srpBatteryBlocks.Count; i++){
            // Get the detailed power data and put it in the temp vars
            if(setTempBatteryData(srpBatteryBlocks[i])){
                // If the battery has juice avabile, then include it in the total
                if(temp_storage_cur > 0 & (srpBatteryBlocks[i] as IMyFunctionalBlock).Enabled & !srpBatteryBlocks[i].DetailedInfo.Contains("recharged")){
                    solbat_output_max += temp_output_max;
                    solbat_output_cur += temp_output_cur;
                }else if(temp_storage_cur > 0){
                    solbat_output_avail_idle += temp_output_max;
                }

                // If it's on and set to recharge and it's not full
                if((srpBatteryBlocks[i] as IMyFunctionalBlock).Enabled & srpBatteryBlocks[i].DetailedInfo.Contains("recharged") & temp_storage_max > temp_storage_cur){
                    solbat_input_max += temp_input_max;
                    solbat_input_cur += temp_input_cur;
                }else if(temp_storage_max > temp_storage_cur){
                    solbat_needs_charge = true;
                }
            }
        }

        solbat_output_avail = solbat_output_max - solbat_output_cur;
        solbat_input_needed = solbat_input_max - solbat_input_max;
        shipbat_input_needed = shipbat_input_max - shipbat_input_cur;

        myDebug("solbat_out_max: "+solbat_output_max.ToString(), debugBat);
        myDebug("solarr_out_max: "+solarr_output_max.ToString(), debugBat);

        myDebug("solbat_input_max: "+solbat_input_max.ToString(), debugBat);
        myDebug("shipbat_input_max: "+shipbat_input_max.ToString(), debugBat);

        // If we keep the batteries on and current power is not optimal, then turn all batteries on and set them to discharge (this is complete powerloss prevention)
        if(keepBatteriesOn & currentPwr < minPwr){
            // Loop through solarbats and make sure they're all on and set to discharge
            for(int i = 0; i < srpBatteryBlocks.Count; i++){
                myDebug("Discharge: "+srpBatteryBlocks[i].CustomName, debugBat);

                // Set it to decharge if it's not already
                if(srpBatteryBlocks[i].DetailedInfo.Contains("recharged")){
                    srpBatteryBlocks[i].GetActionWithName("Recharge").Apply(srpBatteryBlocks[i]);
                }

                // Make sure it's on
                if(!(srpBatteryBlocks[i] as IMyFunctionalBlock).Enabled){
                    srpBatteryBlocks[i].GetActionWithName("OnOff_On").Apply(srpBatteryBlocks[i]);
                }
            }
        }
        // Discharge any full batteries
        else if(keepBatteriesOn){
            // Loop through solarbats and make sure they're all on and set to discharge
            for(int i = 0; i < srpBatteryBlocks.Count; i++){
                if(setTempBatteryData(srpBatteryBlocks[i]) & temp_storage_cur >= temp_storage_max){
                    // Set it to decharge if it's not already
                    if(srpBatteryBlocks[i].DetailedInfo.Contains("recharged")){
                        srpBatteryBlocks[i].GetActionWithName("Recharge").Apply(srpBatteryBlocks[i]);
                    }

                    // Make sure it's on
                    if(!(srpBatteryBlocks[i] as IMyFunctionalBlock).Enabled){
                        srpBatteryBlocks[i].GetActionWithName("OnOff_On").Apply(srpBatteryBlocks[i]);
                    }
                }
            }
        }

        // If solar power + solar battery power is currently overloaded
        if(solarr_output_avail == 0 & solbat_output_avail == 0){
            myDebug("Overloaded", debugBat);

            // No ships need charging, AND the solar array is optimal
            if(shipbat_input_max <= 0 & !ship_needs_charge & currentPwr >= minPwr){
                pwrDeficit = Math.Abs(solarr_output_max - solbat_input_max);
                myDebug("pwrDeficit="+pwrDeficit.ToString(), debugBat);

                //If the power deficit is less than that of one charging array battery, ignore it
                if((shipType == "large" & pwrDeficit < largeBatteryMax) | (shipType == "small" & pwrDeficit < smallBatteryMax)){
                    myDebug("Ignore Overload", debugBat);
                    return;
                }
            }

            // Loop through solar bats, find one that is set to recharge, and turn it off
            for(int i = 0; i < srpBatteryBlocks.Count; i++){
                if(srpBatteryBlocks[i].DetailedInfo.Contains("recharged") & (srpBatteryBlocks[i] as IMyFunctionalBlock).Enabled){
                    // Turn it off
                    srpBatteryBlocks[i].GetActionWithName("OnOff_Off").Apply(srpBatteryBlocks[i]);

                    // We only change 1 battery per tick
                    return;
                }
            }

            // Loop through Solar bats, find one that has fuel, turn it one, and set it to discharge
            for(int i = 0; i < srpBatteryBlocks.Count; i++){
                if(setTempBatteryData(srpBatteryBlocks[i])){
                    //if there is one that has fuel, and is not on or set to discharge, turn it on and set it to discharge
                    if(temp_storage_cur > 0 & (!(srpBatteryBlocks[i] as IMyFunctionalBlock).Enabled | srpBatteryBlocks[i].DetailedInfo.Contains("recharged")) ){
                        // Set it to decharge if it's not already
                        if(srpBatteryBlocks[i].DetailedInfo.Contains("recharged")){
                            srpBatteryBlocks[i].GetActionWithName("Recharge").Apply(srpBatteryBlocks[i]);
                        }
                        // Make sure it's on
                        if(!(srpBatteryBlocks[i] as IMyFunctionalBlock).Enabled){
                            srpBatteryBlocks[i].GetActionWithName("OnOff_On").Apply(srpBatteryBlocks[i]);
                        }
                        // We only change 1 battery per tick
                        return;
                    }
                }
            }

            pwrDeficit = (solarr_output_max + solbat_output_max) - (shipbat_input_cur + solbat_input_cur);
            if(pwrDeficit < -smallBatteryMax & chargeDockedBatteries){
                // Loop through ship bats, find one that is charging and turn it off
                for(int i = 0; i < dockedBatteryBlocks.Count; i++){
                    // If there is one that is charging
                    if(dockedBatteryBlocks[i].IsFunctional & (dockedBatteryBlocks[i] as IMyFunctionalBlock).Enabled & dockedBatteryBlocks[i].DetailedInfo.Contains("recharged")){
                        // turn it off
                        dockedBatteryBlocks[i].GetActionWithName("OnOff_Off").Apply(dockedBatteryBlocks[i]);

                        // We only change 1 battery per tick
                        return;
                    }
                }
            }

            // Nothing could help the overload, we don't want to do anything else though.
            return;
        }

        // If there is no needed input for Ship Batteries, then charge solar bats
        if((shipbat_input_max <= 0 & !ship_needs_charge) | !chargeDockedBatteries){
            myDebug("No Ships", debugBat);

            // Find any functional Solar Bats that are set to discharge and are turned on, and turn them off
            if(!keepBatteriesOn){
                for(int i = 0; i < srpBatteryBlocks.Count; i++){
                    // Currently being depleted
                    if(!srpBatteryBlocks[i].DetailedInfo.Contains("recharged") & (srpBatteryBlocks[i] as IMyFunctionalBlock).Enabled){
                        // Turn it off
                        srpBatteryBlocks[i].GetActionWithName("OnOff_Off").Apply(srpBatteryBlocks[i]);

                        // We only change 1 battery per tick
                        return;
                    }
                }
            }

            // Only recharge solar bats if we're optimal, OR have enough excess to fully accomadate
            if(currentPwr >= minPwr | (shipType == "small" & solarr_output_avail > smallBatteryMax) | (shipType == "large" & solarr_output_avail > largeBatteryMax)){
                // Loop through functional Solar bats, find one that is not full, and set it to recharge
                for(int i = 0; i < srpBatteryBlocks.Count; i++){
                    if(setTempBatteryData(srpBatteryBlocks[i])){
                        // If not full, and power available < power max, where the battery is either off or not charging
                        if(temp_storage_max > temp_storage_cur & solarr_output_max > solarr_output_cur & (!srpBatteryBlocks[i].DetailedInfo.Contains("recharged") | !(srpBatteryBlocks[i] as IMyFunctionalBlock).Enabled) ){
                            // Charge it
                            if(!srpBatteryBlocks[i].DetailedInfo.Contains("recharged")){
                                srpBatteryBlocks[i].GetActionWithName("Recharge").Apply(srpBatteryBlocks[i]);
                            }
                            // Make sure it's on
                            if(!(srpBatteryBlocks[i] as IMyFunctionalBlock).Enabled){
                                srpBatteryBlocks[i].GetActionWithName("OnOff_On").Apply(srpBatteryBlocks[i]);
                            }
                            // We only change 1 battery per tick
                            return;
                        }
                    }
                }
            }
        }

        // If all of the ship bats are set to charge, the maybe turn off or charge some solar bats
        else if(shipbat_input_max == shipbat_input_cur & solarr_output_avail > largeBatteryMax & !ship_needs_charge){
            myDebug("All Ships charging", debugBat);
                //TODO: Do this later, it's not essential
        }

        // If there are ship bats that need charging
        else if(chargeDockedBatteries){
            myDebug("Ships need charging", debugBat);
            // Loop through ship bats, Find one that is not full and if there is power available, set it to charge
            for(int i = 0; i < dockedBatteryBlocks.Count; i++){
                if(setTempBatteryData(dockedBatteryBlocks[i])){
                    // If not full, and power available > power_required and it's off or not set to recharge
                    if(temp_storage_max > temp_storage_cur & (solbat_output_max + solarr_output_max) > (solbat_output_cur + solarr_output_cur) & (!dockedBatteryBlocks[i].DetailedInfo.Contains("recharged") | !(dockedBatteryBlocks[i] as IMyFunctionalBlock).Enabled )  ){
                        // Charge it
                        if(!dockedBatteryBlocks[i].DetailedInfo.Contains("recharged")){
                            dockedBatteryBlocks[i].GetActionWithName("Recharge").Apply(dockedBatteryBlocks[i]);
                        }
                        // Make sure it's on
                        if(!(dockedBatteryBlocks[i] as IMyFunctionalBlock).Enabled){
                            dockedBatteryBlocks[i].GetActionWithName("OnOff_On").Apply(dockedBatteryBlocks[i]);
                        }
                        // We only change 1 battery per tick
                        return;
                    }
                }
            }

            // Loop through solar bats, If there are functional solar bats that are not discharging but can be, then put them on the grid
            for(int i = 0; i < srpBatteryBlocks.Count; i++){
                if(setTempBatteryData(srpBatteryBlocks[i])){
                    // If has charge, and not discharging
                    if(temp_storage_cur > 0 & (!(srpBatteryBlocks[i] as IMyFunctionalBlock).Enabled | srpBatteryBlocks[i].DetailedInfo.Contains("recharged")) ){
                        // Charge it
                        if(!srpBatteryBlocks[i].DetailedInfo.Contains("recharged")){
                            srpBatteryBlocks[i].GetActionWithName("Recharge").Apply(srpBatteryBlocks[i]);
                        }
                        // Make sure it's on
                        if(!(srpBatteryBlocks[i] as IMyFunctionalBlock).Enabled){
                            srpBatteryBlocks[i].GetActionWithName("OnOff_On").Apply(srpBatteryBlocks[i]);
                        }
                        // We only change 1 battery per tick
                        return;
                    }
                }
            }
        }
    }
}

/**
 *	Debug function for echoing strings
 *
 *  @param String message
 *  @param bool show
 * 	@return void
 */
void myDebug(String message, bool show){
    if(show){
        Echo(message);
    }
}

////////////////////////////////////
// Block Filters & Fetches
////////////////////////////////////

/**
 *	Get the currently running ProgrammableBlock
 *
 * 	@return IMyProgrammableBlock
 */
IMyProgrammableBlock getSelf(){
    var runningBlocks = new List<IMyTerminalBlock>();

    GridTerminalSystem.GetBlocksOfType<IMyProgrammableBlock>(runningBlocks,filterRunningPB);

    if(runningBlocks.Count > 1){
        throw new Exception("More than one blocks is running");
    }

    return runningBlocks[0] as IMyProgrammableBlock;
}

/**
 *  Filter for finding the currently running programmable block
 *
 *  @param IMyTerminalBlock block
 * 	@return bool
 */
bool filterRunningPB(IMyTerminalBlock block)
{
    return (block is IMyProgrammableBlock) & (block as IMyProgrammableBlock).IsRunning;
}

/**
 *  Filter for finding functional Solar Panels on this grid
 *
 *  @param IMyTerminalBlock block
 * 	@return bool
 */
bool filterSRPSolarPanels(IMyTerminalBlock block){

    return ((block is IMySolarPanel) & block.IsFunctional & block.CubeGrid == thisProgrammableBlock.CubeGrid);
}

/**
 *  Filter for finding functional Gyros on this grid
 *
 *  @param IMyTerminalBlock block
 * 	@return bool
 */
bool filterSRPGyros(IMyTerminalBlock block){

    return ((block is IMyGyro) & block.IsFunctional & block.CubeGrid == thisProgrammableBlock.CubeGrid);
}

/**
 *  Filter for finding Batteries on this grid
 *
 *  @param IMyTerminalBlock block
 * 	@return bool
 */
bool filterSRPBatteries(IMyTerminalBlock block){

    return ((block is IMyBatteryBlock) & block.IsFunctional & block.CubeGrid == thisProgrammableBlock.CubeGrid);
}

/**
 *  Filter for finding Docked Batteries
 *
 *  @param IMyTerminalBlock block
 * 	@return bool
 */
bool filterDockedBatteries(IMyTerminalBlock block){
    return ((block is IMyBatteryBlock) & block.IsFunctional & block.CubeGrid != thisProgrammableBlock.CubeGrid);
}


//////////////////
// Lazy Storage
//////////////////

// TODO: Write or find a storage class to make this WAY more robust

/**
 *	Save a single variable to the Storage string
 *  Currently only saves a double and only one of two variables
 *
 *  @param String name
 *  @param String name
 * 	@return void
 */
void saveVar(String name, double newValue){
    switch(name){
        case "currentPower":
            double highestPower = getVar("highestPower");
            saveVars(newValue, highestPower);
        break;
        case "highestPower":
            double currentPower = getVar("currentPower");
            saveVars(currentPower, newValue);
        break;
    }
}

/**
 *	Save a all the variables to the Storage string
 *
 *  @param double currentPower
 *  @param double highestPower
 * 	@return void
 */
void saveVars(double currentPower, double highestPower){
    Storage = currentPower.ToString()+"|"+highestPower.ToString();
}

/**
 *	Get a single variable of name from the Storage string
 *
 *  @param string name
 * 	@return double
 */
double getVar(string name){
    if(Storage.Length == 0){
        return 0;
    }

    double myVal;
    switch(name){
        case "currentPower":
            double.TryParse(Storage.Substring(0, Storage.IndexOf('|')),out myVal);
            return myVal;
        break;
        case "highestPower":
            double.TryParse(Storage.Substring(Storage.IndexOf('|')+1),out myVal);
            return myVal;
        break;
    }
    Echo("WTF");
    return 0;
}
