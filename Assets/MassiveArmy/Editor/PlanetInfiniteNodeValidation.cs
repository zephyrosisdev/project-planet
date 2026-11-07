using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
namespace MassiveArmy.EditorTools {
    public static class PlanetInfiniteNodeValidation {
        [Serializable]sealed class Report {public string status="PASS";public List<string> checks=new List<string>();}
        [UnityEditor.MenuItem("Massive Army/Planetary/Validate Infinite Deposits")]
        public static void Run(){
            var report=new Report();
            void Check(bool value,string label){report.checks.Add((value?"PASS ":"FAIL ")+label);if(!value)report.status="FAIL";}
            var node=new ResourceNode{capacity=10,remaining=0};node.MakeInexhaustible();
            Check(node.inexhaustible&&node.remaining==10,"Exhausted deposits become usable again");
            Check(Math.Min(node.Available,250)==250,"Extraction is limited by production rate, not the former reserve");
            for(int i=0;i<10000;i++)node.Consume(250);
            Check(node.remaining==10,"Repeated extraction never depletes an infinite deposit");
            Check(float.IsFinite(node.remaining)&&float.IsFinite(node.capacity),"Saved reserve values stay finite");
            string json=JsonUtility.ToJson(node);var saved=JsonUtility.FromJson<ResourceNode>(json);
            Check(saved.inexhaustible&&saved.remaining==10,"Infinite flag survives serialization");
            var finite=new ResourceNode{capacity=10,remaining=10};finite.Consume(4);
            Check(finite.remaining==6,"Explicit finite fixtures retain their original consumption behaviour");
            finite.Consume(100);Check(finite.remaining==0,"Finite deposits clamp at zero");
            var wallet=new ResourceWallet{Capacity=new ResourceCost{materials=5},Stock=new ResourceCost{materials=4}};
            node.Consume(wallet.Deposit(ResourceKind.Materials,250));
            Check(wallet.Stock.materials==5&&node.remaining==10,"Finite inventory capacity still limits accepted output");
            Directory.CreateDirectory("TestResults/Planetary/InfiniteNodes");File.WriteAllText("TestResults/Planetary/InfiniteNodes/validation.json",JsonUtility.ToJson(report,true));
            if(report.status!="PASS")throw new InvalidOperationException("Infinite deposit validation failed");
        }
    }
}
