using System;
using System.Collections.Generic;
namespace MassiveArmy.Planetary {
    public enum SettlementKind {Capital,CivicTown,ArableFarming,Orchards,Horticulture,Livestock,ForestrySalvage,Construction,MetalMining,RareMineralMining,Metallurgy,Petroleum,Gasworks,Energy,Manufacturing,Electronics,Chemicals,Research,Observatory,Spaceport,Seaport,Logistics,MilitaryGarrison,Fortress,StrategicDeterrent,Prison}
    [Serializable]public sealed class RegionalSettlement {
        public long id,hub,parent;public string name;public SettlementKind kind;public UnityEngine.Vector2 position;
        public double founded,nextGrowth,nextExpansion;public int developmentTier,buildingCount,housing,jobs,failures,focusCursor;public string status="Surveying settlement";
    }
    public sealed class SettlementProfile {
        public readonly SettlementKind kind;public readonly string label;public readonly string[] buildings;public readonly bool coastal,agricultural,military,nuclear;public readonly int target;
        internal SettlementProfile(SettlementKind kind,string label,int target,string[] buildings,bool coast=false,bool agriculture=false,bool military=false,bool nuclear=false){this.kind=kind;this.label=label;this.target=target;this.buildings=buildings;coastal=coast;agricultural=agriculture;this.military=military;this.nuclear=nuclear;}
    }
    public static class SettlementProfiles {
        static SettlementProfile P(SettlementKind k,string label,int target,string focus,bool coast=false,bool farm=false,bool military=false,bool nuclear=false)=>new SettlementProfile(k,label,target,focus.Split(','),coast,farm,military,nuclear);
        public static readonly SettlementProfile[] All={
            P(SettlementKind.Capital,"Capital / administration",120,"civil_council,civil_colonial_admin,civil_market,lab,civil_school,civil_communications,civil_launch_pad,civil_fabricator"),
            P(SettlementKind.CivicTown,"Residential / service town",80,"civil_housing,civil_rental_home,civil_social_home,civil_school,civil_market,civil_pub,civil_park,civil_council"),
            P(SettlementKind.ArableFarming,"Arable farming",56,"civil_farm_wheat,civil_farm_rice,civil_farm_potatoes,civil_food_processing,civil_warehouse,civil_mine_water",farm:true),
            P(SettlementKind.Orchards,"Orchards and fruit",48,"civil_farm_orchard,civil_food_processing,civil_drinks_plant,civil_warehouse",farm:true),
            P(SettlementKind.Horticulture,"Vegetables and oilseed",48,"civil_farm_vegetables,civil_farm_oilseed,civil_farm_flax,civil_food_processing,civil_warehouse",farm:true),
            P(SettlementKind.Livestock,"Livestock and food",48,"civil_beef_shed,civil_butcher,civil_farm_wheat,civil_food_processing,civil_warehouse",farm:true),
            P(SettlementKind.ForestrySalvage,"Forestry and salvage",44,"civil_gathering_camp,civil_fabricator,civil_warehouse,civil_market"),
            P(SettlementKind.Construction,"Construction materials",64,"mine,civil_gathering_camp,civil_fabricator,civil_smelter,material_storage,civil_warehouse"),
            P(SettlementKind.MetalMining,"Metal mining",52,"civil_mine_iron,civil_mine_copper,civil_mine_aluminium,civil_mine_carbon,mine,civil_warehouse"),
            P(SettlementKind.RareMineralMining,"Rare mineral mining",48,"civil_mine_titanium,civil_mine_lithium,civil_mine_rare_earth,civil_mine_platinum,civil_mine_nickel,rare,civil_warehouse"),
            P(SettlementKind.Metallurgy,"Metallurgy and composites",64,"civil_smelter,civil_advanced_materials,civil_composites,civil_mine_iron,civil_mine_carbon,civil_warehouse"),
            P(SettlementKind.Petroleum,"Oil and refining",52,"civil_oil_well,civil_oil_refinery,civil_oil_depot,civil_chemistry,fuel_storage"),
            P(SettlementKind.Gasworks,"Gas processing",48,"civil_mine_methane,civil_gas_depot,civil_fuel_refinery,gas_turbine,fuel_storage"),
            P(SettlementKind.Energy,"Power generation",56,"wind,solar,gas_turbine,reactor,fusion,battery,civil_mine_hydrogen"),
            P(SettlementKind.Manufacturing,"Industrial manufacturing",72,"civil_fabricator,civil_composites,civil_advanced_materials,civil_habitat,civil_warehouse"),
            P(SettlementKind.Electronics,"Electronics production",56,"civil_mine_silicon,civil_mine_copper,civil_electronics,civil_satellite_factory,civil_warehouse"),
            P(SettlementKind.Chemicals,"Chemicals and life support",56,"civil_chemistry,civil_chemical_plant,civil_life_support,civil_mine_sulfur,civil_pharmacy,civil_warehouse"),
            P(SettlementKind.Research,"University and research",64,"lab,advanced_lab,civil_school,civil_council,civil_life_support,civil_park"),
            P(SettlementKind.Observatory,"Astronomy and signals",40,"civil_observatory,civil_radio_telescope,civil_spectroscopy,civil_communications,civil_tracking,lab"),
            P(SettlementKind.Spaceport,"Spaceport and aerospace",72,"civil_spaceport,civil_launch_pad,civil_rocket_assembly,civil_mission_control,civil_payload_integration,civil_satellite_factory,civil_warehouse"),
            P(SettlementKind.Seaport,"Coastal port",64,"civil_port,civil_customs,civil_warehouse,civil_oil_depot,civil_market,civil_food_processing",coast:true),
            P(SettlementKind.Logistics,"Transport and trade hub",64,"civil_freight_station,civil_airport,civil_warehouse,civil_oil_depot,civil_gas_depot,civil_market"),
            P(SettlementKind.MilitaryGarrison,"Military garrison",64,"barracks,light_depot,factory,repair_depot,ordnance,radar_station,autocannon,aa_turret",military:true),
            P(SettlementKind.Fortress,"Border fortress",52,"field_command,radar_station,phased_radar,autocannon,at_turret,sam,missile_defence,shield",military:true),
            P(SettlementKind.StrategicDeterrent,"Strategic deterrent",44,"strategic_lab,silo,missile_defence,point_defence,radar_station,isotope_storage",military:true,nuclear:true),
            P(SettlementKind.Prison,"Justice and corrections",48,"civil_county_jail,civil_courthouse,civil_federal_prison,civil_prison_wing,civil_prison_yard,civil_police_hq")
        };
        public static SettlementProfile Get(SettlementKind kind)=>All[Math.Clamp((int)kind,0,All.Length-1)];
        public static int FocusTarget(string id,int buildings){int stage=Math.Clamp(buildings/24,0,4);return id.StartsWith("civil_farm_")?8+stage*6:1+stage;}
        public static bool IsNuclear(string id)=>id=="silo"||id=="strategic_lab"||id=="isotope"||id=="civil_isotopes";
        public static readonly string[] Common={"civil_warehouse","wind","civil_housing","civil_food_shop","civil_drinks_shop","civil_police_hq","civil_school","civil_council","civil_park","civil_pharmacy","civil_pub","civil_graveyard","civil_funeral_director"};
        // One strategic weapons site for a large country, rather than a silo in every town.
        public static SettlementKind Choose(IReadOnlyList<RegionalSettlement> towns,ColonyState c,int turn,bool fertile,bool coast){
            bool Has(SettlementKind k){for(int i=0;i<towns.Count;i++)if(towns[i].kind==k)return true;return false;}
            if(fertile&&!Has(SettlementKind.ArableFarming))return SettlementKind.ArableFarming;
            if(!Has(SettlementKind.Construction))return SettlementKind.Construction;
            if(c.powerBalance<15&&!Has(SettlementKind.Energy))return SettlementKind.Energy;
            if(!Has(SettlementKind.MetalMining))return SettlementKind.MetalMining;
            if(!Has(SettlementKind.Research))return SettlementKind.Research;
            if(towns.Count>=5&&!Has(SettlementKind.Spaceport))return SettlementKind.Spaceport;
            if(coast&&!Has(SettlementKind.Seaport))return SettlementKind.Seaport;
            var kind=(SettlementKind)(1+turn%(All.Length-1));
            for(int i=0;i<All.Length;i++,kind=(SettlementKind)(1+(int)kind%(All.Length-1))){var p=Get(kind);if(p.agricultural&&!fertile||p.coastal&&!coast||p.nuclear&&(towns.Count<8||Has(kind)))continue;if(!Has(kind))return kind;}
            return SettlementKind.Manufacturing;
        }
    }
}
