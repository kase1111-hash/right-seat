using CommunityToolkit.Mvvm.ComponentModel;
using Guardian.Core;
using Guardian.Desktop.Services;

namespace Guardian.Desktop.ViewModels;

/// <summary>
/// ViewModel for the live telemetry panel showing real-time gauge values.
/// Values are color-coded: normal (green), caution (amber), danger (red).
/// </summary>
public partial class TelemetryPanelViewModel : ObservableObject
{
    private readonly GuardianEngineService _engine;

    // Engine parameters
    [ObservableProperty] private string _rpm1 = "---";
    [ObservableProperty] private string _rpm1Class = "value-normal";
    [ObservableProperty] private string _rpm2 = "---";
    [ObservableProperty] private string _rpm2Class = "value-normal";
    [ObservableProperty] private string _manifoldPressure1 = "---";
    [ObservableProperty] private string _manifoldPressure2 = "---";

    // Temperatures
    [ObservableProperty] private string _cht1 = "---";
    [ObservableProperty] private string _cht1Class = "value-normal";
    [ObservableProperty] private string _egt1 = "---";
    [ObservableProperty] private string _oilTemp1 = "---";
    [ObservableProperty] private string _oilTemp1Class = "value-normal";

    // Pressures
    [ObservableProperty] private string _oilPressure1 = "---";
    [ObservableProperty] private string _oilPressure1Class = "value-normal";

    // Fuel
    [ObservableProperty] private string _fuelLeft = "---";
    [ObservableProperty] private string _fuelRight = "---";
    [ObservableProperty] private string _fuelTotal = "---";
    [ObservableProperty] private string _fuelFlow1 = "---";
    [ObservableProperty] private string _fuelImbalanceClass = "value-normal";

    // Electrical
    [ObservableProperty] private string _mainBusVoltage = "---";
    [ObservableProperty] private string _mainBusVoltageClass = "value-normal";

    // Vacuum
    [ObservableProperty] private string _suctionPressure = "---";
    [ObservableProperty] private string _suctionClass = "value-normal";

    // Environment
    [ObservableProperty] private string _oat = "---";
    [ObservableProperty] private string _altitude = "---";
    [ObservableProperty] private string _airspeed = "---";
    [ObservableProperty] private string _verticalSpeed = "---";

    // Ice
    [ObservableProperty] private string _structuralIce = "---";
    [ObservableProperty] private string _structuralIceClass = "value-normal";
    [ObservableProperty] private string _pitotIce = "---";

    public TelemetryPanelViewModel(GuardianEngineService engine)
    {
        _engine = engine;
    }

    public void UpdateFromSnapshot(TelemetrySnapshot snapshot)
    {
        var profile = _engine.ActiveProfile;
        var eng = profile?.Engine;

        // Engine 1
        var rpm1Val = snapshot.Get(SimVarId.GeneralEngRpm, 1);
        if (rpm1Val is not null)
        {
            Rpm1 = rpm1Val.Value.ToString("F0");
            Rpm1Class = eng is not null && rpm1Val.Value > eng.RpmRange[1] ? "value-danger" : "value-normal";
        }

        var rpm2Val = snapshot.Get(SimVarId.GeneralEngRpm, 2);
        if (rpm2Val is not null) Rpm2 = rpm2Val.Value.ToString("F0");

        var mp1 = snapshot.Get(SimVarId.RecipEngManifoldPressure, 1);
        if (mp1 is not null) ManifoldPressure1 = mp1.Value.ToString("F1") + " inHg";

        // CHT (Rankine → Fahrenheit)
        var cht1Val = snapshot.Get(SimVarId.RecipEngCylinderHeadTemperature, 1);
        if (cht1Val is not null && eng is not null)
        {
            var chtF = UnitsConverter.RankineToFahrenheit(cht1Val.Value);
            Cht1 = chtF.ToString("F0") + " °F";
            Cht1Class = chtF >= eng.ChtRedlineF ? "value-danger"
                : chtF >= eng.ChtNormalRangeF[1] * 0.9 ? "value-caution"
                : "value-normal";
        }

        // EGT
        var egt1Val = snapshot.Get(SimVarId.RecipEngExhaustGasTemperature, 1);
        if (egt1Val is not null)
        {
            var egtF = UnitsConverter.RankineToFahrenheit(egt1Val.Value);
            Egt1 = egtF.ToString("F0") + " °F";
        }

        // Oil temperature (Rankine → Fahrenheit)
        var oilTemp = snapshot.Get(SimVarId.GeneralEngOilTemperature, 1);
        if (oilTemp is not null && eng is not null)
        {
            var oilTempF = UnitsConverter.RankineToFahrenheit(oilTemp.Value);
            OilTemp1 = oilTempF.ToString("F0") + " °F";
            OilTemp1Class = oilTempF >= eng.OilTempRedlineF ? "value-danger"
                : oilTempF >= eng.OilTempNormalRangeF[1] ? "value-caution"
                : "value-normal";
        }

        // Oil pressure
        var oilPress = snapshot.Get(SimVarId.GeneralEngOilPressure, 1);
        if (oilPress is not null && eng is not null)
        {
            OilPressure1 = oilPress.Value.ToString("F0") + " PSI";
            OilPressure1Class = oilPress.Value < eng.OilPressureMinimumPsi ? "value-danger"
                : oilPress.Value < eng.OilPressureNormalPsi[0] ? "value-caution"
                : "value-normal";
        }

        // Fuel
        var fuelL = snapshot.Get(SimVarId.FuelSystemTankQuantity, 0);
        var fuelR = snapshot.Get(SimVarId.FuelSystemTankQuantity, 1);
        if (fuelL is not null) FuelLeft = fuelL.Value.ToString("F1") + " gal";
        if (fuelR is not null) FuelRight = fuelR.Value.ToString("F1") + " gal";
        if (fuelL is not null && fuelR is not null)
        {
            FuelTotal = (fuelL.Value + fuelR.Value).ToString("F1") + " gal";
            double maxQty = Math.Max(fuelL.Value, fuelR.Value);
            if (maxQty > 1)
            {
                double imbalance = Math.Abs(fuelL.Value - fuelR.Value) / (fuelL.Value + fuelR.Value) * 100;
                FuelImbalanceClass = imbalance > 20 ? "value-danger"
                    : imbalance > 10 ? "value-caution"
                    : "value-normal";
            }
        }

        var ff1 = snapshot.Get(SimVarId.RecipEngFuelFlow, 1);
        if (ff1 is not null) FuelFlow1 = ff1.Value.ToString("F1") + " GPH";

        // Electrical
        var busV = snapshot.Get(SimVarId.ElectricalMainBusVoltage);
        if (busV is not null && profile is not null)
        {
            MainBusVoltage = busV.Value.ToString("F1") + " V";
            MainBusVoltageClass = busV.Value < profile.Electrical.MainBusMinimumV ? "value-danger"
                : busV.Value < profile.Electrical.MainBusNormalV[0] ? "value-caution"
                : "value-normal";
        }

        // Vacuum
        var suction = snapshot.Get(SimVarId.SuctionPressure);
        if (suction is not null && profile is not null)
        {
            SuctionPressure = suction.Value.ToString("F2") + " inHg";
            SuctionClass = suction.Value < profile.Vacuum.SuctionMinimumInhg ? "value-danger"
                : suction.Value < profile.Vacuum.SuctionNormalInhg[0] ? "value-caution"
                : "value-normal";
        }

        // Environment
        var oatVal = snapshot.Get(SimVarId.AmbientTemperature);
        if (oatVal is not null) Oat = oatVal.Value.ToString("F0") + " °C";

        var alt = snapshot.Get(SimVarId.IndicatedAltitude);
        if (alt is not null) Altitude = alt.Value.ToString("F0") + " ft";

        var ias = snapshot.Get(SimVarId.AirspeedIndicated);
        if (ias is not null) Airspeed = ias.Value.ToString("F0") + " KIAS";

        var vs = snapshot.Get(SimVarId.VerticalSpeed);
        if (vs is not null) VerticalSpeed = vs.Value.ToString("F0") + " fpm";

        // Ice
        var structIce = snapshot.Get(SimVarId.StructuralIcePct);
        if (structIce is not null)
        {
            StructuralIce = structIce.Value.ToString("F0") + "%";
            StructuralIceClass = structIce.Value > 25 ? "value-danger"
                : structIce.Value > 5 ? "value-caution"
                : "value-normal";
        }

        var pitot = snapshot.Get(SimVarId.PitotIcePct);
        if (pitot is not null) PitotIce = pitot.Value.ToString("F0") + "%";
    }
}
