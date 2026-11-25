using System.Collections.Generic;
using UnityEngine;
namespace BrokenHeart.UI
{
    // Colors and layout adapted from into-the-depths menu_shell.gd.
    public static class MenuSkin
    {
        public static readonly Color Background=new Color32(3,9,24,255),Panel=new Color32(6,26,53,255),Cyan=new Color32(54,217,255,255),Text=new Color32(200,244,255,255),Muted=new Color32(120,169,201,255);
        private static GUIStyle button,selected,danger,input;
        private static readonly Color MainText = new Color32(248, 234, 241, 255);
        private static readonly Color MainAccent = new Color32(213, 148, 180, 255);
        private static GUIStyle mainButton, mainPrimaryButton, mainTitle, mainJourneyText;
        private static GUIStyle pauseButton, pausePrimaryButton;
        private static readonly Dictionary<(int,Color),GUIStyle> labels=new();
        public static void Fill(Rect rect,Color color){var previous=GUI.color;GUI.color=color;GUI.DrawTexture(rect,Texture2D.whiteTexture);GUI.color=previous;}
        public static void Box(Rect rect){Fill(rect,new Color32(8,123,219,255));Fill(new Rect(rect.x+1,rect.y+1,rect.width-2,rect.height-2),Panel);}
        public static void Label(Rect rect,string value,int size=18,Color? color=null)
        {
            var key=(size,color??Text);
            if(!labels.TryGetValue(key,out var style))
            {style=new GUIStyle(GUI.skin.label){fontSize=size,wordWrap=true};style.normal.textColor=key.Item2;labels.Add(key,style);}
            GUI.Label(rect,Localization.T(value),style);
        }
        private static Texture2D Tile(Color fill,Color edge)
        {
            var texture=new Texture2D(3,3,TextureFormat.RGBA32,false){filterMode=FilterMode.Point,hideFlags=HideFlags.HideAndDontSave};
            for(int y=0;y<3;y++)for(int x=0;x<3;x++)texture.SetPixel(x,y,x==1&&y==1?fill:edge);
            texture.Apply();return texture;
        }
        private static void Init()
        {
            if(button!=null)return;
            button=new GUIStyle(GUI.skin.button){fontSize=17,border=new RectOffset(1,1,1,1),padding=new RectOffset(10,10,5,5),wordWrap=true};
            button.normal.background=Tile(new Color32(12,44,81,255),new Color32(8,123,219,255));button.normal.textColor=Text;
            button.hover.background=Tile(new Color32(16,72,119,255),Cyan);button.hover.textColor=Color.white;
            button.active.background=Tile(new Color32(8,123,190,255),Cyan);button.active.textColor=Color.white;
            selected=new GUIStyle(button);selected.normal.background=Tile(new Color32(8,123,190,255),Cyan);selected.normal.textColor=Color.white;
            danger=new GUIStyle(button);danger.normal.background=Tile(new Color32(12,44,81,255),new Color32(255,85,118,255));
            input=new GUIStyle(GUI.skin.textField){fontSize=20,padding=new RectOffset(10,10,6,6),border=new RectOffset(1,1,1,1)};
            input.normal.background=Tile(Background,Muted);input.normal.textColor=Text;input.focused.background=Tile(Background,Cyan);input.focused.textColor=Color.white;
        }
        public static bool Button(Rect rect,string label,bool active=false,bool destructive=false)
        {Init();return GUI.Button(rect,Localization.T(label),destructive?danger:active?selected:button);}
        public static string Input(Rect rect,string value,int maxLength=64){Init();return GUI.TextField(rect,value??"",maxLength,input);}

        private static Texture2D RoundedButton(Color fill, Color border)
        {
            const int size = 18;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave
            };
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float dx = Mathf.Max(0, Mathf.Abs(x - 8.5f) - 3);
                float dy = Mathf.Max(0, Mathf.Abs(y - 8.5f) - 3);
                float distance = Mathf.Sqrt(dx * dx + dy * dy);
                var pixel = Color.Lerp(fill, border, Mathf.Clamp01(distance - 4));
                pixel.a *= Mathf.Clamp01(6 - distance);
                texture.SetPixel(x, y, pixel);
            }
            texture.Apply(false, true);
            return texture;
        }

        public static bool PauseButton(Rect rect, string label, bool primary = false)
        {
            if (pauseButton == null)
            {
                pauseButton = new GUIStyle(GUI.skin.button)
                {
                    fontSize = 20,
                    alignment = TextAnchor.MiddleCenter,
                    border = new RectOffset(8, 8, 8, 8),
                    padding = new RectOffset(14, 14, 4, 4)
                };
                pauseButton.normal.background = RoundedButton(new Color32(11, 35, 59, 248), new Color32(65, 145, 184, 255));
                pauseButton.normal.textColor = Text;
                pauseButton.hover.background = RoundedButton(new Color32(19, 67, 103, 255), Cyan);
                pauseButton.hover.textColor = Color.white;
                pauseButton.active = pauseButton.hover;
                pauseButton.focused = pauseButton.hover;

                pausePrimaryButton = new GUIStyle(pauseButton) { fontStyle = FontStyle.Bold };
                pausePrimaryButton.normal.background = RoundedButton(new Color32(20, 83, 127, 255), new Color32(96, 218, 250, 255));
                pausePrimaryButton.normal.textColor = Color.white;
            }
            return GUI.Button(rect, Localization.T(label), primary ? pausePrimaryButton : pauseButton);
        }

        public static void MainTitle(Rect rect, string title)
        {
            if (mainTitle == null)
            {
                mainTitle = new GUIStyle(GUI.skin.label)
                {
                    fontSize = 58,
                    fontStyle = FontStyle.Bold,
                    padding = new RectOffset(),
                    alignment = TextAnchor.MiddleLeft
                };
                mainTitle.normal.textColor = MainText;
            }

            var color = GUI.contentColor;
            GUI.contentColor = new Color(0.35f, 0.24f, 0.31f, 0.8f);
            GUI.Label(new Rect(rect.x + 2, rect.y + 3, rect.width, rect.height), title, mainTitle);
            GUI.contentColor = color;
            GUI.Label(rect, title, mainTitle);
        }

        public static bool MainButton(Rect rect, string label, bool primary = false)
        {
            if (mainButton == null)
            {
                mainButton = new GUIStyle(GUI.skin.button)
                {
                    fontSize = 17,
                    alignment = TextAnchor.MiddleLeft,
                    border = new RectOffset(1, 1, 1, 1),
                    padding = new RectOffset(15, 12, 0, 0)
                };
                mainButton.normal.background = Tile(new Color32(24, 19, 32, 246), new Color32(93, 73, 101, 255));
                mainButton.normal.textColor = MainText;
                mainButton.hover.background = Tile(new Color32(59, 37, 58, 255), MainAccent);
                mainButton.hover.textColor = Color.white;
                mainButton.active.background = Tile(new Color32(81, 43, 68, 255), MainAccent);
                mainButton.active.textColor = Color.white;
                mainButton.focused = mainButton.hover;

                mainPrimaryButton = new GUIStyle(mainButton);
                mainPrimaryButton.normal.background = Tile(new Color32(78, 39, 66, 255), MainAccent);
                mainPrimaryButton.fontStyle = FontStyle.Bold;
            }

            return GUI.Button(rect, Localization.T(label), primary ? mainPrimaryButton : mainButton);
        }

        public static void MainJourney(Rect rect, string description, bool canContinue)
        {
            Fill(rect, new Color32(93, 73, 101, 255));
            Fill(new Rect(rect.x + 1, rect.y + 1, rect.width - 2, rect.height - 2), new Color32(20, 15, 27, 245));
            if (!canContinue)
            {
                Label(new Rect(rect.x + 12, rect.y + 14, rect.width - 24, 25), description, 17, MainText);
                return;
            }

            if (mainJourneyText == null)
            {
                mainJourneyText = new GUIStyle(GUI.skin.label)
                {
                    fontSize = 15,
                    wordWrap = true,
                    clipping = TextClipping.Clip,
                    padding = new RectOffset()
                };
                mainJourneyText.normal.textColor = MainText;
            }

            Label(new Rect(rect.x + 12, rect.y + 2, rect.width - 24, 17), "ОСТАННЯ ПОДОРОЖ", 12, MainAccent);
            GUI.Label(new Rect(rect.x + 16, rect.y + 19, rect.width - 32, 31), description, mainJourneyText);
        }

        public static void Landscape(Rect rect)
        {
            GUI.BeginGroup(rect);
            float w=rect.width,h=rect.height;
            Fill(new Rect(0,0,w,h),new Color32(10,24,46,255));
            for(int i=0;i<30;i++)
            {
                float x=(i*97+23)%Mathf.Max(1,w),y=(i*43+13)%(h*0.45f);
                float glow=0.35f+0.2f*Mathf.Sin(Time.unscaledTime*0.6f+i);
                Fill(new Rect(x,y,2,2),new Color(0.65f,0.84f,1,glow));
            }
            Fill(new Rect(w*0.71f,h*0.13f,36,36),new Color32(153,211,220,255));
            Fill(new Rect(w*0.71f+9,h*0.13f-4,31,32),new Color32(10,24,46,255));
            for(int layer=0;layer<3;layer++)
                for(int x=0;x<w;x+=4)
                {
                    float y=h*(0.49f+layer*0.12f)+Mathf.Sin(x*0.012f+layer*2)*h*0.09f+Mathf.Sin(x*0.027f+layer)*h*0.035f;
                    Fill(new Rect(x,y,4,h-y),layer==0?new Color32(28,57,75,255):layer==1?new Color32(19,46,58,255):new Color32(11,31,41,255));
                }
            for(int i=0;i<8;i++)
            {
                float x=w*(i+0.4f)/8,y=h*0.80f+Mathf.Sin(i*2)*h*0.04f;
                Fill(new Rect(x,y-45,5,70),new Color32(7,22,30,255));
                for(int row=0;row<5;row++)Fill(new Rect(x-row*5,y-70+row*13,6+row*10,16),new Color32(9,33,38,255));
            }
            GUI.EndGroup();
        }
        public static void Backdrop(float width=1280,float height=720)
        {
            Fill(new Rect(0,0,width,height),Background);Fill(new Rect(width*0.42f,0,width*0.58f,height),new Color(0,0.22f,0.65f,0.12f));
            Fill(new Rect(0,height*0.72f,width,height*0.28f),new Color(0,0.55f,1,0.06f));
            for(int x=0;x<width;x+=100)Fill(new Rect(x,0,1,height),new Color(0.05f,0.42f,0.9f,0.06f));
            for(int y=0;y<height;y+=100)Fill(new Rect(0,y,width,1),new Color(0.05f,0.42f,0.9f,0.06f));
        }
    }
}

