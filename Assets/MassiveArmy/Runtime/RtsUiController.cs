using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace MassiveArmy
{
    // Retained presentation lifecycle. No unit state is copied into the visual tree.
    public sealed class RtsUiController : IDisposable
    {
        readonly Action<VisualElement> build;
        readonly Action refresh;
        readonly Func<bool> inBattle;
        readonly GameObject host;
        readonly PanelRenderer renderer;
        readonly PanelSettings panel;
        int lastVersion=-1;
        readonly List<VisualElement> blockers=new List<VisualElement>();
        public VisualElement Root {get;private set;}
        public float Scale {get;private set;}
        public bool TextFocused {get {for(var element=Root?.focusController?.focusedElement as VisualElement;element!=null;element=element.parent)if(element is TextField||element.ClassListContains("unity-base-text-field"))return true;return false;}}
        public RtsUiController(BattleApp app):this(app.transform,app.BuildToolkitUi,app.RefreshToolkitUi,()=>app.State=="battle"){}
        public RtsUiController(Transform parent,Action<VisualElement> build,Action refresh,Func<bool> inBattle)
        {
            this.build=build;this.refresh=refresh;this.inBattle=inBattle;Scale=PlayerPrefs.GetFloat("Army.UiScale",1);
            panel=UnityEngine.Object.Instantiate(Resources.Load<PanelSettings>("MassiveArmy/UI/FrontPanel"));
            panel.scale=Scale;
            host=new GameObject("Synthetic Front UI");host.transform.SetParent(parent,false);
            renderer=host.AddComponent<PanelRenderer>();
            renderer.panelSettings=panel;
            renderer.visualTreeAsset=Resources.Load<VisualTreeAsset>("MassiveArmy/UI/Front");
        }
        public void Attach()
        {
            renderer.RegisterUIReloadCallback((PanelRenderer.VersionedUIReloadCallback)Reload);
        }
        void Reload(PanelRenderer source,VisualElement root,int version)
        {
            if(ReferenceEquals(Root,root)&&lastVersion==version)return;lastVersion=version;Root=root;
            // PanelRenderer's Editor container can have zero height until explicitly stretched.
            Root.StretchToParentSize();Root.style.flexGrow=1;blockers.Clear();build(root);
            // Buttons inherit TextElement, but only editable fields own gameplay keys.
            Root.RegisterCallback<KeyDownEvent>(e=>{
                if(inBattle()&&!TextFocused&&(e.keyCode==KeyCode.W||e.keyCode==KeyCode.A||e.keyCode==KeyCode.S||e.keyCode==KeyCode.D||
                    e.keyCode==KeyCode.UpArrow||e.keyCode==KeyCode.DownArrow||e.keyCode==KeyCode.LeftArrow||e.keyCode==KeyCode.RightArrow)){Root.focusController.IgnoreEvent(e);e.StopImmediatePropagation();}
            },TrickleDown.TrickleDown);
            Root.RegisterCallback<NavigationMoveEvent>(e=>{
                if(inBattle()&&!TextFocused&&e.direction!=NavigationMoveEvent.Direction.Next&&e.direction!=NavigationMoveEvent.Direction.Previous){Root.focusController.IgnoreEvent(e);e.StopImmediatePropagation();}
            },TrickleDown.TrickleDown);
        }
        public void Block(VisualElement element){blockers.Add(element);}
        public bool Blocks(Vector2 screen)
        {
            if(Root?.panel==null)return true;
            Vector2 p=RuntimePanelUtils.ScreenToPanel(Root.panel,new Vector2(screen.x,Screen.height-screen.y));
            for(var hit=Root.panel.Pick(p);hit!=null;hit=hit.parent)
                if(hit is Button||hit is ScrollView||hit.ClassListContains("unity-base-field")||hit.ClassListContains("unity-base-dropdown")||hit.ClassListContains("unity-dropdown-menu"))return true;
            foreach(var element in blockers)
                if(element.resolvedStyle.display!=DisplayStyle.None&&element.visible&&element.worldBound.Contains(p))return true;
            return false;
        }
        public void SetScale(float scale){Scale=Mathf.Clamp(scale,.85f,1.25f);panel.scale=Scale;}
        public void SetRenderTarget(RenderTexture target){panel.targetTexture=target;panel.clearColor=target!=null;panel.colorClearValue=Color.clear;}
        public void Tick(){if(Root!=null)refresh();}
        public string Diagnostics => $"UI Toolkit PanelRenderer; scale {Scale:0.00}; panel {panel.referenceResolution}; root {Root?.childCount}; fonts Rajdhani / Inter";
        public void Dispose()
        {
            if(renderer!=null)renderer.UnregisterUIReloadCallback((PanelRenderer.VersionedUIReloadCallback)Reload);
            ArmyLifetime.Destroy(host);ArmyLifetime.Destroy(panel);Root=null;
        }
    }

    // Small cached vector surface for angular framing, paths and selection outlines.
    public sealed class RtsUiDrawing : VisualElement
    {
        public readonly List<(Vector2 a,Vector2 b,Color colour)> Lines=new List<(Vector2,Vector2,Color)>();
        public RtsUiDrawing(){pickingMode=PickingMode.Ignore;generateVisualContent+=Draw;}
        void Draw(MeshGenerationContext context)
        {
            var p=context.painter2D;p.lineWidth=2;
            foreach(var line in Lines){p.strokeColor=line.colour;p.BeginPath();p.MoveTo(line.a);p.LineTo(line.b);p.Stroke();}
        }
    }

    public sealed class RtsUiFrame : VisualElement
    {
        public RtsUiFrame()
        {
            pickingMode=PickingMode.Ignore;style.position=Position.Absolute;this.StretchToParentSize();
            generateVisualContent+=context=>
            {
                float w=contentRect.width,h=contentRect.height;if(w<30||h<30)return;
                var p=context.painter2D;p.lineWidth=1;p.strokeColor=new Color(.06f,.40f,.53f);
                p.BeginPath();p.MoveTo(new Vector2(1,15));p.LineTo(new Vector2(15,1));p.LineTo(new Vector2(w-15,1));p.LineTo(new Vector2(w-1,15));p.LineTo(new Vector2(w-1,h-15));p.LineTo(new Vector2(w-15,h-1));p.LineTo(new Vector2(15,h-1));p.LineTo(new Vector2(1,h-15));p.ClosePath();p.Stroke();
                p.lineWidth=2;p.strokeColor=new Color(.06f,.83f,1);p.BeginPath();p.MoveTo(new Vector2(1,35));p.LineTo(new Vector2(1,15));p.LineTo(new Vector2(15,1));p.LineTo(new Vector2(50,1));p.Stroke();
                p.BeginPath();p.MoveTo(new Vector2(w-50,h-1));p.LineTo(new Vector2(w-15,h-1));p.LineTo(new Vector2(w-1,h-15));p.LineTo(new Vector2(w-1,h-35));p.Stroke();
            };
        }
    }
}
