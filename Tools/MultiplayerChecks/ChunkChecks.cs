using System;
using System.Linq;
using MassiveArmy.Industrial;

static class ChunkChecks {
    internal static int Run(){
        int checks=0;void Check(bool value,string why){if(!value)throw new Exception(why);checks++;}
        bool Reject(Action action){try{action();return false;}catch(ArgumentException){return true;}}
        var random=new Random(439);var bytes=new byte[170003];random.NextBytes(bytes);var sender=new SurvivalChunkSender(1,bytes);var receiver=new SurvivalChunkReceiver();
        var first=sender.Preview(137);Check(first.offset==0&&sender.Preview(137).offset==0,"Preview never advances delivery before transport success");
        Check(receiver.Accept(first,0)==null&&receiver.Accept(first,1)==null,"Exact duplicate fragments neither corrupt nor advance assembly");sender.Commit(first);
        byte[] completed=null;int fragments=1;while(!sender.Complete){var chunk=sender.Preview(4093);completed=receiver.Accept(chunk,fragments++);sender.Commit(chunk);}
        Check(completed!=null&&completed.SequenceEqual(bytes)&&receiver.BufferedBytes==0,"Multi-fragment arbitrary binary payload round-trips with bounded assembly and digest verification");
        Check(receiver.Accept(first,100)==null,"Completed transfer replay cannot reopen an assembly");
        Check(Reject(()=>new SurvivalChunkSender(2,new byte[SurvivalChunkSender.MaximumBytes+1])),"Sender rejects an oversized logical snapshot");
        var next=new SurvivalChunkSender(2,bytes);var head=next.Preview(100);Check(Reject(()=>receiver.Accept(new SurvivalChunk{transfer=2,total=int.MaxValue,count=1,offset=0,hash=head.hash,payload="AA=="},101)),"Receiver rejects declared memory overflow before allocation");
        Check(Reject(()=>receiver.Accept(new SurvivalChunk{transfer=2,total=200,offset=int.MaxValue,count=100,hash=head.hash,payload=head.payload},101)),"Offset overflow is rejected");
        receiver.Accept(head,102);next.Commit(head);var skipped=next.Preview(100);skipped.offset+=100;Check(Reject(()=>receiver.Accept(skipped,103)),"Out-of-order fragments cannot fill missing ranges");
        var conflict=next.Preview(100);conflict.offset=0;Check(Reject(()=>receiver.Accept(conflict,104)),"A conflicting duplicate is rejected");
        var bad=next.Preview(100);bad.total--;Check(Reject(()=>receiver.Accept(bad,105)),"Transfer size cannot change halfway through a snapshot");
        var replacement=new SurvivalChunkSender(3,new byte[]{1,2,3,4});var small=replacement.Preview(4);Check(receiver.Accept(small,106).SequenceEqual(new byte[]{1,2,3,4})&&receiver.BufferedBytes==0,"A newer transfer replaces abandoned partial state without retaining both buffers");
        Check(receiver.Accept(head,107)==null,"Late fragments from an abandoned transfer cannot replace newer state");
        var corruptSender=new SurvivalChunkSender(4,new byte[]{3,8,9});var corrupt=corruptSender.Preview(3);corrupt.payload=Convert.ToBase64String(new byte[]{4,8,9});Check(Reject(()=>receiver.Accept(corrupt,108))&&receiver.BufferedBytes==0,"Digest failure rejects the entire snapshot and releases memory");
        receiver.Clear();Check(receiver.BufferedBytes==0&&receiver.Accept(first,0)==null,"Disconnect clears receiver identity and bounded partial memory");
        receiver.Cancel(first.transfer);Check(receiver.BufferedBytes==0&&receiver.Accept(first,1)==null,"Interest/authorization cancellation releases partial memory and blocks late fragments");
        var delivery=new SurvivalInterestDelivery();var old=delivery.Stage(9,"old",10,1,0,100);var recent=delivery.Stage(9,"new",20,1,0,100);delivery.Commit(new[]{recent});delivery.Commit(new[]{old});
        Check(delivery.IsCurrent(9,new SurvivalInterestDelivery.Fingerprint{data="new"}),"A slow transfer commit cannot roll back a newer delivered revision");
        return checks;
    }
}
