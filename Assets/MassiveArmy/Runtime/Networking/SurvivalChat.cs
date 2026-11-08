using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Unity.Services.Vivox;
using UnityEngine;
using UnityEngine.InputSystem;

namespace MassiveArmy.Networking {
    [DefaultExecutionOrder(-300)]
    internal sealed class SurvivalChat:MonoBehaviour {
        // Vivox is process-global. Old logout must complete before a new login.
        static readonly SemaphoreSlim serviceGate=new SemaphoreSlim(1,1);
        readonly Queue<string> history=new Queue<string>();readonly SurvivalChatInput input=new SurvivalChatInput();
        SurvivalNetworkSession session;string textChannel,voiceChannel;bool initialized,stopped,held,changingVoice,focusInput;
        Task stopTask;double nextPosition,nextVoiceAttempt;int voiceBody;
        CursorLockMode previousCursorLock;bool previousCursorVisible;
        internal bool TextReady {get;private set;}internal bool VoiceReady {get;private set;}
        internal string Status {get;private set;}="Connecting chat";
        internal bool InputMuted {get{try{return initialized&&VivoxService.Instance.IsInputDeviceMuted;}catch{return false;}}}
        internal event Action<string,string> TextReceived;
        public bool BlockGameplay=>input.Blocks(Time.frameCount);
        public void Bind(SurvivalNetworkSession owner){session=owner;_ = Connect();}
        static string Channel(string value){using(var hash=SHA256.Create())return "sf_"+BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(value))).Replace("-","").Substring(0,32);}
        void MuteImmediate(){held=false;try{VivoxService.Instance?.MuteInputDevice();}catch(Exception){}}
        async Task Connect(){
            await serviceGate.WaitAsync();
            try{
                if(stopped)return;MuteImmediate();await VivoxService.Instance.InitializeAsync();initialized=true;if(stopped)return;
                MuteImmediate();VivoxService.Instance.ChannelMessageReceived+=Message;
                if(!VivoxService.Instance.IsLoggedIn)await VivoxService.Instance.LoginAsync(new LoginOptions{DisplayName=session.Latest?.vitals?.name??PlayerPrefs.GetString("Survival.DisplayName","Survivor")});
                if(stopped)return;MuteImmediate();await VivoxService.Instance.SetChannelTransmissionModeAsync(TransmissionMode.None);if(stopped)return;
                textChannel=Channel(session.Config.worldId+"/"+session.Config.instanceId+"/text");
                await VivoxService.Instance.JoinGroupChannelAsync(textChannel,ChatCapability.TextOnly,new ChannelOptions{MakeActiveChannelUponJoining=false});if(stopped)return;
                TextReady=true;Status="Text connected";Line(Status);
            }catch(Exception e){Status="Chat unavailable: "+e.Message;Line(Status);}
            finally{serviceGate.Release();}
            if(!stopped&&TextReady)await UpdateVoice();
        }
        async Task UpdateVoice(){
            if(changingVoice||stopped||!TextReady||session.Latest?.motion==null)return;
            changingVoice=true;VoiceReady=false;MuteImmediate();await serviceGate.WaitAsync();
            try{
                if(stopped)return;await VivoxService.Instance.SetChannelTransmissionModeAsync(TransmissionMode.None);
                if(voiceChannel!=null){await VivoxService.Instance.LeaveChannelAsync(voiceChannel);voiceChannel=null;}
                if(stopped)return;int body=session.Latest.motion.pose.body;string channel=Channel(session.Config.worldId+"/"+session.Config.instanceId+"/body/"+body);
                await VivoxService.Instance.JoinPositionalChannelAsync(channel,ChatCapability.AudioOnly,new Channel3DProperties(60,10,1,AudioFadeModel.InverseByDistance),new ChannelOptions{MakeActiveChannelUponJoining=false});
                if(stopped)return;voiceChannel=channel;voiceBody=body;MuteImmediate();VoiceReady=true;Status="Text and positional voice connected";Line(Status);
            }catch(Exception e){Status="Voice unavailable; text remains connected: "+e.Message;Line(Status);nextVoiceAttempt=Time.realtimeSinceStartupAsDouble+5;}
            finally{changingVoice=false;serviceGate.Release();}
        }
        void Message(VivoxMessage message){if(!stopped&&message!=null&&message.ChannelName==textChannel){Line(message.SenderDisplayName+": "+message.MessageText);TextReceived?.Invoke(message.SenderPlayerId,message.MessageText);}}
        void Line(string message){history.Enqueue(message.Length>512?message.Substring(0,512):message);while(history.Count>8)history.Dequeue();}
        void Update(){
            if(stopped)return;var keys=Keyboard.current;
            if(keys!=null&&Application.isFocused){
                if(keys.enterKey.wasPressedThisFrame||keys.numpadEnterKey.wasPressedThisFrame){
                    bool wasTyping=input.Typing;string message=input.Enter(Time.frameCount);
                    if(wasTyping){RestoreInputCursor();if(message!=null)_ = SendText(message);}
                    else{focusInput=true;Mute();previousCursorLock=Cursor.lockState;previousCursorVisible=Cursor.visible;Cursor.lockState=CursorLockMode.None;Cursor.visible=true;}
                }else if(keys.escapeKey.wasPressedThisFrame&&input.Escape(Time.frameCount))RestoreInputCursor();
            }
            if(!TextReady)return;
            if(!changingVoice&&session.Latest?.motion!=null&&(!VoiceReady||voiceBody!=session.Latest.motion.pose.body)&&Time.realtimeSinceStartupAsDouble>=nextVoiceAttempt)_ = UpdateVoice();
            bool speak=input.CanTransmit(Time.frameCount,Application.isFocused&&!session.NetworkMenuOpen,keys!=null&&keys[SurvivalNetworkSession.Bindings.pushToTalk].isPressed,VoiceReady);
            if(speak!=held){held=speak;if(!speak)try{VivoxService.Instance.MuteInputDevice();}catch(Exception){} _ = UpdateTransmission();}
            if(!VoiceReady||changingVoice||session.Predicted==null||Time.realtimeSinceStartupAsDouble<nextPosition)return;nextPosition=Time.realtimeSinceStartupAsDouble+.1;
            var pose=session.Predicted;var feet=session.ClientWorld?.PlayerForValidation?.PreciseBodyPosition??new MassiveArmy.Planetary.Double3(pose.pose.x,pose.pose.y,pose.pose.z);var position=feet.Float;var camera=session.ClientWorld?.View;
            try{VivoxService.Instance.Set3DPosition(position,position,camera!=null?camera.transform.forward:Vector3.forward,camera!=null?camera.transform.up:position.normalized,voiceChannel);}catch(Exception e){Line("Voice position: "+e.Message);nextPosition+=5;}
        }
        async Task UpdateTransmission(){
            await serviceGate.WaitAsync();
            try{
                if(stopped||!initialized||!VivoxService.Instance.IsLoggedIn)return;
                bool speak=input.CanTransmit(Time.frameCount,Application.isFocused&&!session.NetworkMenuOpen,held,VoiceReady);string channel=voiceChannel;
                await VivoxService.Instance.SetChannelTransmissionModeAsync(speak?TransmissionMode.Single:TransmissionMode.None,speak?channel:null);
                if(speak&&channel==voiceChannel&&input.CanTransmit(Time.frameCount,Application.isFocused&&!session.NetworkMenuOpen,held,VoiceReady)&&!stopped)VivoxService.Instance.UnmuteInputDevice();else VivoxService.Instance.MuteInputDevice();
            }catch(Exception e){MuteImmediate();Line("Voice transmit: "+e.Message);}
            finally{serviceGate.Release();}
        }
        internal async Task<bool> SendText(string message){
            message=message?.Trim();if(string.IsNullOrEmpty(message))return false;
            if(message.Length>256||!TextReady||stopped){Line("Message not sent: chat unavailable or message too long");return false;}
            // Finish an in-flight send before leaving channels or logging out.
            await serviceGate.WaitAsync();
            try{if(!TextReady||stopped)return false;await VivoxService.Instance.SendChannelTextMessageAsync(textChannel,message);return true;}
            catch(Exception e){Line("Message not sent: "+e.Message);return false;}
            finally{serviceGate.Release();}
        }
        void RestoreInputCursor(){GUIUtility.keyboardControl=0;Cursor.lockState=previousCursorLock;Cursor.visible=previousCursorVisible;}
        public void Mute(){MuteImmediate();if(initialized&&!stopped)_ = UpdateTransmission();}
        public Task StopAsync(){if(stopTask!=null)return stopTask;stopped=true;MuteImmediate();TextReady=VoiceReady=false;if(input.Typing)RestoreInputCursor();input.Clear();try{VivoxService.Instance.ChannelMessageReceived-=Message;}catch(Exception){}stopTask=Cleanup();return stopTask;}
        async Task Cleanup(){
            await serviceGate.WaitAsync();
            try{if(initialized&&VivoxService.Instance.IsLoggedIn){
                try{await VivoxService.Instance.SetChannelTransmissionModeAsync(TransmissionMode.None);}catch(Exception e){Debug.LogWarning("Chat transmission cleanup: "+e.Message);}
                try{await VivoxService.Instance.LeaveAllChannelsAsync();}catch(Exception e){Debug.LogWarning("Chat channel cleanup: "+e.Message);}
                try{await VivoxService.Instance.LogoutAsync();}catch(Exception e){Debug.LogWarning("Chat logout: "+e.Message);}
            }}finally{serviceGate.Release();}
        }
        void OnApplicationFocus(bool focused){if(!focused)Mute();}
        void OnDestroy(){_ = StopAsync();}
        void OnGUI(){
            if(stopped)return;float width=Mathf.Min(660,Screen.width-32),height=history.Count*22;
            GUI.Box(new Rect(16,Screen.height-75-height,width,height+8),"");int row=0;foreach(var line in history)GUI.Label(new Rect(24,Screen.height-71-height+row++*22,width-16,22),line);
            if(!input.Typing)return;GUI.SetNextControlName("survival-chat-input");input.Text=GUI.TextField(new Rect(16,Screen.height-57,width,32),input.Text,256);if(focusInput){GUI.FocusControl("survival-chat-input");focusInput=false;}
        }
    }
}
