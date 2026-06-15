// VRChat World Build Size Optimizer - Unity Editor Extension
using UnityEngine;
using UnityEditor;
using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;

namespace VRCWorldOptimizer
{
    public class TextureInfo  { public Texture2D texture; public string path; public long sizeByte; public int width, height; public TextureImporterFormat format; public bool hasMipmap, isReadable, crunchCompressed, usedInScene, selected; public long estimatedSaveByte; }
    public class MeshInfo     { public Mesh mesh; public string path; public long sizeByte; public bool isReadWrite, usedInScene, selected; public ModelImporterMeshCompression compression; public int blendShapeCount; public long estimatedSaveByte; }
    public class AudioInfo    { public AudioClip clip; public string path; public long sizeByte; public AudioCompressionFormat format; public AudioClipLoadType loadType; public float quality; public bool usedInScene, selected; public long estimatedSaveByte; }
    public class UnusedAssetInfo { public string path; public long sizeByte; public string assetType; public bool selected; }

    public class ReflectionProbeInfo
    {
        public string path; public long sizeByte; public int currentRes; public bool selected; public long estimatedSaveByte;
    }

    public class FontInfo
    {
        public string path; public long sizeByte; public string fontType; // "TTF" or "SDF"
        public int atlasWidth, atlasHeight;
        public bool includeFontData; // TTF only
        public bool selected; public long estimatedSaveByte; public string issue;
    }

    [System.Serializable]
    public class BackupEntry
    {
        public string id, label, backupDir;
        public System.DateTime createdAt;
        public List<string> metaPaths        = new List<string>();
        public List<string> deletedOrigPaths = new List<string>();
        public List<string> deletedBackPaths = new List<string>();
        public List<string> assetBodyOrigPaths = new List<string>(); // .metaではなく本体ファイルのパス
        public List<string> assetBodyBackPaths = new List<string>(); // バックアップ先パス
    }

    public class VRCWorldBuildOptimizer : EditorWindow
    {
        private const string VERSION = "ver0.75-beta";
        private enum Lang { JA, EN }
        private Lang _lang = Lang.JA;
        private string T(string ja, string en) => _lang == Lang.JA ? ja : en;

        private enum Tab { Dashboard, Textures, Meshes, Audio, ReflProbe, Fonts, Cleanup, Backup, Report }
        private Tab _tab = Tab.Dashboard;
        private Vector2 _scroll, _backupScroll;

        private List<TextureInfo>         _textures  = new List<TextureInfo>();
        private List<MeshInfo>            _meshes    = new List<MeshInfo>();
        private List<AudioInfo>           _audios    = new List<AudioInfo>();
        private List<UnusedAssetInfo>     _unused    = new List<UnusedAssetInfo>();
        private List<ReflectionProbeInfo> _refProbes = new List<ReflectionProbeInfo>();
        private List<FontInfo>            _fonts     = new List<FontInfo>();
        private List<BackupEntry>         _backups   = new List<BackupEntry>();

        private bool _analyzed = false, _isProcessing = false, _stylesReady = false;
        private float _progress = 0f;

        private int  _texMaxSize        = 1024;
        private int  _texCrunchQuality  = 50;
        private bool _texEnableCrunch   = true;
        private bool _texMipmapStream   = true;
        private bool _texDisableRW      = true;
        // ★ プラットフォーム別も同時に書き換えるか
        private bool _texOverridePlatforms = true;
        private bool _iosCompatible        = false; // ★ iOS版対応モード: iPhoneでアップロード不可なCrunch圧縮を除外

        private bool _meshDisableRW = true;
        private ModelImporterMeshCompression _meshCompression = ModelImporterMeshCompression.Low;

        private AudioCompressionFormat _audioFormat   = AudioCompressionFormat.Vorbis;
        private AudioClipLoadType      _audioLoadType = AudioClipLoadType.CompressedInMemory;
        private float _audioQuality = 0.4f;

        private int _probeTargetRes = 256;
        private string _searchTex="",_searchMesh="",_searchAudio="",_searchProbe="",_searchFont="",_searchUnused=""; // ファイル名検索

        private static readonly string BACKUP_ROOT = "ProjectSettings/VRCOptimizer/backups";

        private static readonly Color CH=new Color(.11f,.11f,.16f), CC=new Color(.18f,.18f,.24f), CA=new Color(.35f,.7f,1f),
                                      CG=new Color(.3f,.9f,.45f),   CY=new Color(1f,.8f,.2f),    CR=new Color(1f,.38f,.38f),
                                      CR0=new Color(.16f,.16f,.22f),CR1=new Color(.2f,.2f,.27f);
        private GUIStyle _sTitle, _sSec, _sCard, _sBtn, _sPrim, _sDanger, _sRestore, _sLabel, _sBold, _sSmall, _sTab, _sTabA, _sRow0, _sRow1, _sSceneBtn;

        [MenuItem("Tools/VRC World Build Optimizer")]
        public static void Open() { var w = GetWindow<VRCWorldBuildOptimizer>("VRC Build Optimizer"); w.minSize = new Vector2(980,640); }
        private void OnEnable() { LoadBackupManifests(); }

        // ===== スタイル =====
        private void InitStyles()
        {
            if(_stylesReady) return; _stylesReady=true;
            _sTitle  = S(EditorStyles.label,   s=>{s.fontSize=20;s.fontStyle=FontStyle.Bold;s.normal=new GUIStyleState{textColor=CA};s.margin=new RectOffset(0,0,8,4);});
            _sSec    = S(EditorStyles.label,   s=>{s.fontSize=12;s.fontStyle=FontStyle.Bold;s.normal=new GUIStyleState{textColor=Color.white};s.margin=new RectOffset(0,0,6,2);});
            _sCard   = S(GUI.skin.box,          s=>{s.normal=new GUIStyleState{background=MkTex(CC)};s.padding=new RectOffset(12,12,10,10);s.margin=new RectOffset(0,0,4,4);});
            _sBtn    = S(GUI.skin.button,       s=>{s.fontSize=11;s.fixedHeight=26;s.normal=new GUIStyleState{textColor=Color.white};});
            _sPrim   = S(GUI.skin.button,       s=>{s.fontSize=12;s.fontStyle=FontStyle.Bold;s.fixedHeight=32;s.normal=new GUIStyleState{background=MkTex(new Color(.2f,.5f,.9f)),textColor=Color.white};});
            _sDanger = S(GUI.skin.button,       s=>{s.fontSize=12;s.fontStyle=FontStyle.Bold;s.fixedHeight=32;s.normal=new GUIStyleState{background=MkTex(new Color(.7f,.18f,.18f)),textColor=Color.white};});
            _sRestore= S(GUI.skin.button,       s=>{s.fontSize=12;s.fontStyle=FontStyle.Bold;s.fixedHeight=32;s.normal=new GUIStyleState{background=MkTex(new Color(.18f,.55f,.3f)),textColor=Color.white};});
            _sLabel  = S(EditorStyles.label,   s=>{s.wordWrap=true;});
            _sBold   = S(EditorStyles.label,   s=>{s.fontStyle=FontStyle.Bold;s.normal=new GUIStyleState{textColor=Color.white};});
            _sSmall  = S(EditorStyles.miniLabel,s=>{s.normal=new GUIStyleState{textColor=new Color(.65f,.65f,.65f)};});
            _sTab    = S(EditorStyles.toolbarButton,s=>{s.fontSize=10;s.fixedHeight=28;});
            _sTabA   = S(_sTab, s=>{s.fontStyle=FontStyle.Bold;s.normal=new GUIStyleState{textColor=CA};});
            _sRow0   = S(GUI.skin.box, s=>{s.normal=new GUIStyleState{background=MkTex(CR0)};s.margin=new RectOffset(0,0,1,1);s.padding=new RectOffset(6,6,3,3);});
            _sRow1   = S(GUI.skin.box, s=>{s.normal=new GUIStyleState{background=MkTex(CR1)};s.margin=new RectOffset(0,0,1,1);s.padding=new RectOffset(6,6,3,3);});
            _sSceneBtn = new GUIStyle(_sPrim){fontSize=11,fixedHeight=28};_sSceneBtn.normal=new GUIStyleState{background=MkTex(new Color(.12f,.46f,.30f)),textColor=Color.white};
        }
        private GUIStyle S(GUIStyle src,Action<GUIStyle> mod){var s=new GUIStyle(src);mod(s);return s;}
        private Texture2D MkTex(Color c){var t=new Texture2D(2,2);t.SetPixels(Enumerable.Repeat(c,4).ToArray());t.Apply();t.hideFlags=HideFlags.HideAndDontSave;return t;}

        // ===== OnGUI =====
        private void OnGUI()
        {
            InitStyles();
            // ===== ヘッダー =====
            EditorGUI.DrawRect(new Rect(0,0,position.width,62),CH);
            GUI.Label(new Rect(12,10,380,28), "VRC World Build Optimizer", _sTitle);
            GUI.Label(new Rect(16,38,200,14), VERSION,
                new GUIStyle(EditorStyles.miniLabel){normal=new GUIStyleState{textColor=new Color(.5f,.5f,.58f)}});
            {
                var ss = new GUIStyle(EditorStyles.boldLabel){normal=new GUIStyleState{textColor=_analyzed?CG:CY}};
                var st = _analyzed ? T("解析完了","Analyzed") : T("未解析","Not Analyzed");
                var sw = ss.CalcSize(new GUIContent(st)).x;
                GUI.Label(new Rect(position.width-sw-14,20,sw+4,22), st, ss);
            }
            GUILayout.Space(62);

            EditorGUI.DrawRect(new Rect(0,62,position.width,30),CH);
            GUILayout.BeginHorizontal(EditorStyles.toolbar);
            TB(Tab.Dashboard,T("ダッシュボード","Dashboard"));TB(Tab.Textures,T("テクスチャ","Textures"));TB(Tab.Meshes,T("メッシュ","Meshes"));
            TB(Tab.Audio,T("オーディオ","Audio"));TB(Tab.ReflProbe,T("反射プローブ","Refl.Probe"));TB(Tab.Fonts,T("フォント","Fonts"));
            TB(Tab.Cleanup,T("不要アセット","Cleanup"));TB(Tab.Backup,T("バックアップ","Backup"));TB(Tab.Report,T("レポート","Report"));
            GUILayout.FlexibleSpace();
            var newLang=(Lang)EditorGUILayout.EnumPopup(_lang,GUILayout.Width(48),GUILayout.Height(22));
            if(newLang!=_lang){_lang=newLang;_stylesReady=false;Repaint();}
            GUILayout.Space(4);
            GUILayout.EndHorizontal();

            if(_isProcessing){EditorGUI.DrawRect(new Rect(0,92,position.width*_progress,4),CA);EditorGUI.DrawRect(new Rect(position.width*_progress,92,position.width*(1-_progress),4),CC);Repaint();}

            GUILayout.Space(6);_scroll=GUILayout.BeginScrollView(_scroll);
            switch(_tab){
                case Tab.Dashboard: DrawDashboard();break; case Tab.Textures: DrawTextures();break;
                case Tab.Meshes:    DrawMeshes();break;    case Tab.Audio:    DrawAudio();break;
                case Tab.ReflProbe: DrawReflProbe();break; case Tab.Fonts:    DrawFonts();break;
                case Tab.Cleanup:   DrawCleanup();break;   case Tab.Backup:   DrawBackup();break;
                case Tab.Report:    DrawReport();break;
            }
            GUILayout.EndScrollView();
        }
        private void TB(Tab t,string l){bool a=_tab==t;if(GUILayout.Toggle(a,l,a?_sTabA:_sTab,GUILayout.Width(98)))_tab=t;}

        // ===== ダッシュボード =====
        private void DrawDashboard()
        {
            Pad(()=>{
                GUILayout.Space(6);
                EditorGUILayout.HelpBox(T("プロジェクト内の全アセットを解析して最適化できる項目を検出します。\nVRChatワールドの目標 Build size: 200 MB 以下","Analyzes all assets and detects optimization targets.\nVRChat World target Build size: under 200 MB"),MessageType.Info);
                GUILayout.Space(6);
                GUILayout.BeginHorizontal();
                if(GUILayout.Button(T("プロジェクト全体を解析","Analyze Project"),_sPrim,GUILayout.Height(40)))AnalyzeProject();
                GUILayout.Space(8);GUI.enabled=_analyzed;
                if(GUILayout.Button(T("全項目を一括適用","Apply All"),_sDanger,GUILayout.Height(40)))
                    if(EditorUtility.DisplayDialog(T("確認","Confirm"),T("選択中の全最適化を適用します。\nAssetDatabase が更新されます。\n（適用前に自動バックアップします）","Apply all optimizations.\n(Auto backup will be created)"),T("適用する","Apply"),T("キャンセル","Cancel")))ApplyAll();
                GUI.enabled=true;GUILayout.EndHorizontal();

                // シーン使用中のアセットのみに適用ボタン
                GUI.enabled=_analyzed;
                GUILayout.BeginHorizontal();
                GUILayout.FlexibleSpace();
                if(GUILayout.Button(T("✦ シーン使用中のアセットのみに適用","✦ Apply to Scene-Used Assets Only"),_sSceneBtn??_sPrim))
                    if(EditorUtility.DisplayDialog(T("確認","Confirm"),T("現在のシーンで使用中のアセットのみに最適化を適用します。\n未使用アセットはスキップされます。\n（適用前に自動バックアップします）","Optimizes only assets used in the current scene.\nUnused assets will be skipped.\n(Auto backup will be created)"),T("適用する","Apply"),T("キャンセル","Cancel")))ApplySceneOnly();
                GUILayout.EndHorizontal();
                GUI.enabled=true;

                if(!_analyzed){GUILayout.Space(16);DrawHowItWorks();return;}
                GUILayout.Space(12);SL(T("解析サマリー","Summary"));
                GUILayout.BeginHorizontal();
                StatCard("テクスチャ",  _textures.Count+T(" 件"," files"),  _textures.Sum(x=>x.sizeByte),  _textures.Where(x=>x.selected).Sum(x=>x.estimatedSaveByte),  CA);
                GUILayout.Space(4);
                StatCard("メッシュ",    _meshes.Count+T(" 件"," files"),    _meshes.Sum(x=>x.sizeByte),    _meshes.Where(x=>x.selected).Sum(x=>x.estimatedSaveByte),    CG);
                GUILayout.Space(4);
                StatCard("オーディオ",  _audios.Count+T(" 件"," files"),    _audios.Sum(x=>x.sizeByte),    _audios.Where(x=>x.selected).Sum(x=>x.estimatedSaveByte),    CY);
                GUILayout.EndHorizontal();GUILayout.Space(4);
                GUILayout.BeginHorizontal();
                StatCard("反射プローブ",_refProbes.Count+T(" 件"," files"), _refProbes.Sum(x=>x.sizeByte), _refProbes.Where(x=>x.selected).Sum(x=>x.estimatedSaveByte), new Color(1f,.5f,.1f));
                GUILayout.Space(4);
                StatCard("フォント",    _fonts.Count+T(" 件"," files"),     _fonts.Sum(x=>x.sizeByte),     _fonts.Where(x=>x.selected).Sum(x=>x.estimatedSaveByte),     new Color(.7f,.5f,1f));
                GUILayout.Space(4);
                StatCard("未使用",      _unused.Count+T(" 件"," files"),    _unused.Sum(x=>x.sizeByte),    _unused.Where(x=>x.selected).Sum(x=>x.sizeByte),             CR);
                GUILayout.EndHorizontal();GUILayout.Space(10);
                long total=_textures.Where(x=>x.selected).Sum(x=>x.estimatedSaveByte)+_meshes.Where(x=>x.selected).Sum(x=>x.estimatedSaveByte)
                          +_audios.Where(x=>x.selected).Sum(x=>x.estimatedSaveByte)+_refProbes.Where(x=>x.selected).Sum(x=>x.estimatedSaveByte)
                          +_fonts.Where(x=>x.selected).Sum(x=>x.estimatedSaveByte)+_unused.Where(x=>x.selected).Sum(x=>x.sizeByte);
                using(new GUILayout.VerticalScope(_sCard)){GUILayout.Label(T("合計削減見込みサイズ","Total Estimated Savings"),_sSec);GUILayout.Label("  "+FS(total)+T(" 削減可能"," can be reduced"),new GUIStyle(EditorStyles.label){fontSize=18,fontStyle=FontStyle.Bold,normal=new GUIStyleState{textColor=CG}});GUILayout.Space(2);GUILayout.Label(T("※ 実際の削減量はビルド環境によって異なります。","* Actual savings may vary by build environment."),_sSmall);}
                GUILayout.Space(10);SL(T("最適化設定","Settings"));DrawSettings();
            });
        }

        private void DrawSettings()
        {
            using(new GUILayout.VerticalScope(_sCard)){
                GUILayout.Label(T("iOS対応","iOS"),_sBold);
                _iosCompatible=GUILayout.Toggle(_iosCompatible,T(" iOS対応（iOS対応を行わない場合はこのチェックは不要です）"," iOS Support (no need to check this if you are not targeting iOS)"));
                if(_iosCompatible)EditorGUILayout.HelpBox(T("iOS対応が有効です。Crunch圧縮（iPhoneビルド非対応）はスキップされ、既存のCrunch圧縮も適用時に解除されます。SDFフォントのCrunch圧縮もスキップされます。","iOS support ON: Crunch compression (unsupported on iPhone builds) is skipped and existing Crunch is removed on apply. SDF font Crunch is also skipped."),MessageType.Info);
                else EditorGUILayout.HelpBox(T("iOS向けにアップロードする場合はONにしてください","Turn ON if you upload to iOS"),MessageType.None);
                GUILayout.Space(8);
                GUILayout.Label(T("テクスチャ","Texture"),_sBold);
                GUILayout.BeginHorizontal();
                GUILayout.Label(T("最大解像度","Max Size"),GUILayout.Width(70));
                _texMaxSize=EditorGUILayout.IntPopup(_texMaxSize,new[]{"256","512","1024","2048","4096"},new[]{256,512,1024,2048,4096},GUILayout.Width(70));
                GUILayout.Space(10);_texEnableCrunch  =GUILayout.Toggle(_texEnableCrunch,  T(" Crunch圧縮"," Crunch Compression"));
                GUILayout.Space(10);_texDisableRW     =GUILayout.Toggle(_texDisableRW,     T(" Read/Write無効化"," Disable Read/Write"));
                GUILayout.Space(10);_texMipmapStream  =GUILayout.Toggle(_texMipmapStream,  T(" Mipmapストリーミング"," Mipmap Streaming"));
                GUILayout.EndHorizontal();
                GUILayout.BeginHorizontal();
                // ★ 修正点: プラットフォーム別オーバーライドも書き換えるオプション
                _texOverridePlatforms=GUILayout.Toggle(_texOverridePlatforms,T(" Standalone/Android のプラットフォーム別設定も上書き（推奨）"," Override Per-Platform Settings (Recommended)"));
                GUILayout.EndHorizontal();

                GUILayout.Space(6);GUILayout.Label(T("メッシュ","Mesh"),_sBold);
                GUILayout.BeginHorizontal();
                _meshDisableRW=GUILayout.Toggle(_meshDisableRW,T(" Read/Write無効化"," Disable Read/Write"));
                GUILayout.Space(14);GUILayout.Label(T("圧縮レベル","Compression"),GUILayout.Width(70));
                _meshCompression=(ModelImporterMeshCompression)EditorGUILayout.EnumPopup(_meshCompression,GUILayout.Width(90));
                GUILayout.EndHorizontal();

                GUILayout.Space(6);GUILayout.Label(T("オーディオ","Audio"),_sBold);
                GUILayout.BeginHorizontal();
                GUILayout.Label(T("圧縮形式","Format"),GUILayout.Width(60));_audioFormat=(AudioCompressionFormat)EditorGUILayout.EnumPopup(_audioFormat,GUILayout.Width(100));
                GUILayout.Space(10);GUILayout.Label(T("ロード方式","Load Type"),GUILayout.Width(70));_audioLoadType=(AudioClipLoadType)EditorGUILayout.EnumPopup(_audioLoadType,GUILayout.Width(160));
                GUILayout.Space(10);GUILayout.Label(T("品質:","Quality:")+Mathf.RoundToInt(_audioQuality*100)+"%",GUILayout.Width(80));
                _audioQuality=GUILayout.HorizontalSlider(_audioQuality,0.1f,1f,GUILayout.Width(100));
                GUILayout.EndHorizontal();

                GUILayout.Space(6);GUILayout.Label(T("反射プローブ","Probe"),_sBold);
                GUILayout.BeginHorizontal();
                GUILayout.Label(T("プローブ目標解像度","Probe Target"),GUILayout.Width(120));
                _probeTargetRes=EditorGUILayout.IntPopup(_probeTargetRes,new[]{"64","128","256","512","1024"},new[]{64,128,256,512,1024},GUILayout.Width(70));
                GUILayout.EndHorizontal();
            }
        }

        private void StatCard(string title,string count,long size,long save,Color accent)
        {
            using(new GUILayout.VerticalScope(_sCard,GUILayout.MinWidth(150))){
                GUILayout.Label(title,new GUIStyle(_sSec){normal=new GUIStyleState{textColor=accent}});
                GUILayout.Label("  "+count,_sBold);GUILayout.Label(T("  現在: ","  Size: ")+FS(size),_sSmall);
                GUI.color=CG;GUILayout.Label(T("  削減: ","  Save: ")+FS(save),_sSmall);GUI.color=Color.white;
            }
        }

        private void DrawHowItWorks()
        {
            using(new GUILayout.VerticalScope(_sCard)){
                GUILayout.Label(T("このツールでできること","What this tool does"),_sSec);GUILayout.Space(4);
                foreach(var s in new[]{
                    "テクスチャ   ―― 最大解像度制限（プラットフォーム別含む）・Crunch圧縮・Mipmap・Read/Write無効化",
                    "メッシュ     ―― Read/Write無効化・圧縮レベル設定",
                    "オーディオ   ―― Vorbis圧縮・ロード方式変更・品質調整",
                    "反射プローブ ―― Cubemap EXR の解像度を削減（最大60MB超削減）",
                    "フォント     ―― TTF ビルド除外 / SDF アトラスCrunch圧縮（最大90%削減・文字データ保持）",
                    "不要アセット ―― 未使用テクスチャ・メッシュ・Audioの検出と一括削除",
                    "バックアップ ―― 適用前に自動バックアップ、ワンクリックで元に戻せる",
                    "レポート     ―― 最適化レポートを .txt ファイルに保存",
                }){GUILayout.Space(2);GUILayout.Label(s,_sLabel);}
                GUILayout.Space(6);EditorGUILayout.HelpBox(T("VRChatワールドの目標 Build size：200MB 以下","VRChat World target Build size: under 200MB"),MessageType.Warning);
            }
        }

        // ===== テクスチャ =====
                private string SearchBar(string val){
            GUILayout.BeginHorizontal(EditorStyles.toolbar);
            GUILayout.Label(T("ファイル名検索","Search by name"),GUILayout.Width(110));
            var nv=GUILayout.TextField(val??"",GUILayout.MinWidth(180));
            if(GUILayout.Button(T("クリア","Clear"),EditorStyles.toolbarButton,GUILayout.Width(54)))nv="";
            GUILayout.EndHorizontal();GUILayout.Space(2);
            return nv;
        }
        private static bool MatchName(string path,string q){return string.IsNullOrEmpty(q)||Path.GetFileName(path).IndexOf(q,System.StringComparison.OrdinalIgnoreCase)>=0;}
        private static bool MatchPath(string path,string q){return string.IsNullOrEmpty(q)||path.IndexOf(q,System.StringComparison.OrdinalIgnoreCase)>=0;}
        private void DrawTextures()
        {
            Pad(()=>{
                GUILayout.Space(6);if(!_analyzed){NA();return;}
                SL("テクスチャ ("+_textures.Count+T(" 件 / 要最適化: "," files / Needs: ")+_textures.Count(t=>t.estimatedSaveByte>0)+" 件)");
                SelButtons(()=>_textures.ForEach(t=>t.selected=true),()=>_textures.ForEach(t=>t.selected=false),()=>_textures.ForEach(t=>t.selected=t.estimatedSaveByte>0));
                int n=_textures.Count(t=>t.selected);GUI.enabled=n>0;
                if(GUILayout.Button(T("選択 ","Apply ")+n+T(" 件を適用"," selected"),_sPrim,GUILayout.Width(180)))
                    if(EditorUtility.DisplayDialog("確認",n+" 件のテクスチャを最適化します。\n（自動バックアップされます）","適用","キャンセル"))ApplyTextures();
                GUI.enabled=true;GUILayout.EndHorizontal();GUILayout.Space(4);
                                _searchTex=SearchBar(_searchTex);
using(new GUILayout.HorizontalScope(_sRow0)){GUILayout.Label("",GUILayout.Width(20));GUILayout.Label(T("使用中","Used"),GUILayout.Width(38));GUILayout.Label(T("テクスチャ名","Name"),GUILayout.Width(190));GUILayout.Label(T("サイズ","Size"),GUILayout.Width(75));GUILayout.Label(T("実解像度","Resolution"),GUILayout.Width(80));GUILayout.Label(T("最大設定","MaxSize"),GUILayout.Width(70));GUILayout.Label(T("PC上書","PC Ovr"),GUILayout.Width(60));GUILayout.Label(T("問題点","Issues"),GUILayout.MinWidth(150));GUILayout.Label(T("削減見込み","Savings"),GUILayout.Width(90));}
                for(int i=0;i<_textures.Count;i++){var t=_textures[i];if(!MatchName(t.path,_searchTex))continue;var iss=TexIssues(t);using(new GUILayout.HorizontalScope(i%2==0?_sRow0:_sRow1)){t.selected=GUILayout.Toggle(t.selected,"",GUILayout.Width(20));GUI.color=t.usedInScene?CG:CR;GUILayout.Label(t.usedInScene?"○":"×",new GUIStyle(EditorStyles.label){alignment=TextAnchor.MiddleCenter,fontStyle=FontStyle.Bold,normal=new GUIStyleState{textColor=t.usedInScene?CG:CR}},GUILayout.Width(38));GUI.color=Color.white;if(GUILayout.Button(Path.GetFileName(t.path),EditorStyles.label,GUILayout.Width(190)))Selection.activeObject=t.texture;GUILayout.Label(FS(t.sizeByte),GUILayout.Width(75));GUILayout.Label(t.width+"x"+t.height,GUILayout.Width(80));GUILayout.Label(t.format.ToString().Replace("AutomaticCompressed","Auto"),GUILayout.Width(70));var pcImp=AssetImporter.GetAtPath(t.path)as TextureImporter;var pcSet=pcImp?.GetPlatformTextureSettings("Standalone");GUI.color=(pcSet!=null&&pcSet.overridden&&pcSet.maxTextureSize>_texMaxSize)?CR:CG;GUILayout.Label(pcSet!=null&&pcSet.overridden?pcSet.maxTextureSize+"px":"—",GUILayout.Width(60));GUI.color=iss.Count>0?CY:CG;GUILayout.Label(iss.Count>0?string.Join(", ",iss):T("✓ 最適","✓ OK"),GUILayout.MinWidth(160));GUI.color=t.estimatedSaveByte>0?CG:Color.gray;GUILayout.Label(t.estimatedSaveByte>0?"-"+FS(t.estimatedSaveByte):"-",GUILayout.Width(90));GUI.color=Color.white;}}
            });
        }

        // ★ 修正: Standalone オーバーライドも含めて問題検出
        private List<string> TexIssues(TextureInfo t)
        {
            var r=new List<string>();
            var imp=AssetImporter.GetAtPath(t.path)as TextureImporter;
            // 実サイズが超過しているか（デフォルト or プラットフォーム設定に起因）
            if(t.width>_texMaxSize||t.height>_texMaxSize)r.Add(T("解像度超過(実","Res>max(")+t.width+"px)");
            // Standalone オーバーライドが maxTextureSize より大きい
            if(imp!=null){var pc=imp.GetPlatformTextureSettings("Standalone");if(pc.overridden&&pc.maxTextureSize>_texMaxSize)r.Add(T("PC設定","PC>")+pc.maxTextureSize+"px超過");}
            if(t.isReadable)r.Add(T("R/W有効","R/W On"));
            if(!t.crunchCompressed&&_texEnableCrunch)r.Add(T("Crunch未使用","No Crunch"));
            return r;
        }

        // ===== メッシュ =====
        private void DrawMeshes()
        {
            Pad(()=>{
                GUILayout.Space(6);if(!_analyzed){NA();return;}
                SL("メッシュ ("+_meshes.Count+T(" 件 / 要最適化: "," files / Needs: ")+_meshes.Count(m=>m.estimatedSaveByte>0)+" 件)");
                SelButtons(()=>_meshes.ForEach(m=>m.selected=true),()=>_meshes.ForEach(m=>m.selected=false),()=>_meshes.ForEach(m=>m.selected=m.estimatedSaveByte>0));
                int n=_meshes.Count(m=>m.selected);GUI.enabled=n>0;
                if(GUILayout.Button(T("選択 ","Apply ")+n+T(" 件を適用"," selected"),_sPrim,GUILayout.Width(180)))
                    if(EditorUtility.DisplayDialog("確認",n+" 件のメッシュを最適化します。\n（自動バックアップされます）","適用","キャンセル"))ApplyMeshes();
                GUI.enabled=true;GUILayout.EndHorizontal();GUILayout.Space(4);
                                _searchMesh=SearchBar(_searchMesh);
using(new GUILayout.HorizontalScope(_sRow0)){GUILayout.Label("",GUILayout.Width(20));GUILayout.Label(T("使用中","Used"),GUILayout.Width(38));GUILayout.Label(T("メッシュ名","Name"),GUILayout.Width(210));GUILayout.Label(T("サイズ","Size"),GUILayout.Width(75));GUILayout.Label(T("頂点数","Vertices"),GUILayout.Width(80));GUILayout.Label("BlendShape",GUILayout.Width(90));GUILayout.Label(T("圧縮","Compress"),GUILayout.Width(90));GUILayout.Label(T("問題点","Issues"),GUILayout.MinWidth(130));GUILayout.Label(T("削減見込み","Savings"),GUILayout.Width(90));}
                for(int i=0;i<_meshes.Count;i++){var m=_meshes[i];if(!MatchName(m.path,_searchMesh))continue;var iss=new List<string>();if(m.isReadWrite)iss.Add(T("R/W有効","R/W On"));if(m.compression==ModelImporterMeshCompression.Off)iss.Add(T("圧縮なし","No Compress"));using(new GUILayout.HorizontalScope(i%2==0?_sRow0:_sRow1)){m.selected=GUILayout.Toggle(m.selected,"",GUILayout.Width(20));GUI.color=m.usedInScene?CG:CR;GUILayout.Label(m.usedInScene?"○":"×",new GUIStyle(EditorStyles.label){alignment=TextAnchor.MiddleCenter,fontStyle=FontStyle.Bold,normal=new GUIStyleState{textColor=m.usedInScene?CG:CR}},GUILayout.Width(38));GUI.color=Color.white;if(GUILayout.Button(Path.GetFileName(m.path),EditorStyles.label,GUILayout.Width(210)))Selection.activeObject=m.mesh;GUILayout.Label(FS(m.sizeByte),GUILayout.Width(75));GUILayout.Label(m.mesh!=null?m.mesh.vertexCount.ToString("N0"):"-",GUILayout.Width(80));GUILayout.Label(m.blendShapeCount.ToString(),GUILayout.Width(90));GUILayout.Label(m.compression.ToString(),GUILayout.Width(90));GUI.color=iss.Count>0?CY:CG;GUILayout.Label(iss.Count>0?string.Join(", ",iss):T("✓ 最適","✓ OK"),GUILayout.MinWidth(150));GUI.color=m.estimatedSaveByte>0?CG:Color.gray;GUILayout.Label(m.estimatedSaveByte>0?"-"+FS(m.estimatedSaveByte):"-",GUILayout.Width(90));GUI.color=Color.white;}}
            });
        }

        // ===== オーディオ =====
        private void DrawAudio()
        {
            Pad(()=>{
                GUILayout.Space(6);if(!_analyzed){NA();return;}
                SL("オーディオ ("+_audios.Count+T(" 件 / 要最適化: "," files / Needs: ")+_audios.Count(a=>a.estimatedSaveByte>0)+" 件)");
                SelButtons(()=>_audios.ForEach(a=>a.selected=true),()=>_audios.ForEach(a=>a.selected=false),()=>_audios.ForEach(a=>a.selected=a.estimatedSaveByte>0));
                int n=_audios.Count(a=>a.selected);GUI.enabled=n>0;
                if(GUILayout.Button(T("選択 ","Apply ")+n+T(" 件を適用"," selected"),_sPrim,GUILayout.Width(180)))
                    if(EditorUtility.DisplayDialog("確認",n+" 件のオーディオを最適化します。\n（自動バックアップされます）","適用","キャンセル"))ApplyAudios();
                GUI.enabled=true;GUILayout.EndHorizontal();GUILayout.Space(4);
                                _searchAudio=SearchBar(_searchAudio);
using(new GUILayout.HorizontalScope(_sRow0)){GUILayout.Label("",GUILayout.Width(20));GUILayout.Label(T("使用中","Used"),GUILayout.Width(38));GUILayout.Label(T("クリップ名","Name"),GUILayout.Width(200));GUILayout.Label(T("サイズ","Size"),GUILayout.Width(75));GUILayout.Label(T("圧縮形式","Format"),GUILayout.Width(120));GUILayout.Label(T("ロード方式","Load Type"),GUILayout.Width(150));GUILayout.Label(T("問題点","Issues"),GUILayout.MinWidth(120));GUILayout.Label(T("削減見込み","Savings"),GUILayout.Width(90));}
                for(int i=0;i<_audios.Count;i++){var a=_audios[i];if(!MatchName(a.path,_searchAudio))continue;var iss=new List<string>();if(a.format==AudioCompressionFormat.PCM)iss.Add(T("非圧縮PCM","Uncompressed"));if(a.loadType==AudioClipLoadType.DecompressOnLoad)iss.Add(T("展開ロード","Decompress"));using(new GUILayout.HorizontalScope(i%2==0?_sRow0:_sRow1)){a.selected=GUILayout.Toggle(a.selected,"",GUILayout.Width(20));GUI.color=a.usedInScene?CG:CR;GUILayout.Label(a.usedInScene?"○":"×",new GUIStyle(EditorStyles.label){alignment=TextAnchor.MiddleCenter,fontStyle=FontStyle.Bold,normal=new GUIStyleState{textColor=a.usedInScene?CG:CR}},GUILayout.Width(38));GUI.color=Color.white;if(GUILayout.Button(Path.GetFileName(a.path),EditorStyles.label,GUILayout.Width(200)))Selection.activeObject=a.clip;GUILayout.Label(FS(a.sizeByte),GUILayout.Width(75));GUILayout.Label(a.format.ToString(),GUILayout.Width(120));GUILayout.Label(a.loadType.ToString(),GUILayout.Width(160));GUI.color=iss.Count>0?CY:CG;GUILayout.Label(iss.Count>0?string.Join(", ",iss):T("✓ 最適","✓ OK"),GUILayout.MinWidth(130));GUI.color=a.estimatedSaveByte>0?CG:Color.gray;GUILayout.Label(a.estimatedSaveByte>0?"-"+FS(a.estimatedSaveByte):"-",GUILayout.Width(90));GUI.color=Color.white;}}
            });
        }

        // ===== 反射プローブ =====
        private void DrawReflProbe()
        {
            Pad(()=>{
                GUILayout.Space(6);if(!_analyzed){NA();return;}
                SL("反射プローブ ("+_refProbes.Count+" 件 / "+FS(_refProbes.Sum(x=>x.sizeByte))+")");
                EditorGUILayout.HelpBox("ReflectionProbeのベイク済みCubemap EXRです。解像度を下げることで大幅に削減できます。\n目標解像度はダッシュボードの設定で変更可能（推奨: 256）。適用後は必ず見た目を確認してください。",MessageType.Warning);
                GUILayout.Space(4);
                SelButtons(()=>_refProbes.ForEach(p=>p.selected=true),()=>_refProbes.ForEach(p=>p.selected=false),()=>_refProbes.ForEach(p=>p.selected=p.estimatedSaveByte>0));
                int n=_refProbes.Count(p=>p.selected);GUI.enabled=n>0;
                if(GUILayout.Button("選択 "+n+" 件を適用 (目標解像度: "+_probeTargetRes+"px)",_sPrim,GUILayout.Width(300)))
                    if(EditorUtility.DisplayDialog("確認",n+" 件のReflectionProbeを最大"+_probeTargetRes+"pxに削減します。\n（自動バックアップされます）\n適用後は見た目を必ず確認してください。","適用","キャンセル"))ApplyReflProbes();
                GUI.enabled=true;GUILayout.EndHorizontal();GUILayout.Space(4);
                                _searchProbe=SearchBar(_searchProbe);
using(new GUILayout.HorizontalScope(_sRow0)){GUILayout.Label("",GUILayout.Width(20));GUILayout.Label(T("ファイル名","File"),GUILayout.Width(280));GUILayout.Label("サイズ",GUILayout.Width(80));GUILayout.Label(T("現在解像度","Current"),GUILayout.Width(90));GUILayout.Label(T("目標解像度","Target"),GUILayout.Width(90));GUILayout.Label(T("削減見込み","Savings"),GUILayout.Width(90));}
                for(int i=0;i<_refProbes.Count;i++){var p=_refProbes[i];if(!MatchName(p.path,_searchProbe))continue;using(new GUILayout.HorizontalScope(i%2==0?_sRow0:_sRow1)){p.selected=GUILayout.Toggle(p.selected,"",GUILayout.Width(20));GUILayout.Label(Path.GetFileName(p.path),GUILayout.Width(280));GUILayout.Label(FS(p.sizeByte),GUILayout.Width(80));GUI.color=p.currentRes>_probeTargetRes?CY:CG;GUILayout.Label(p.currentRes+"px",GUILayout.Width(90));GUILayout.Label(_probeTargetRes+"px",GUILayout.Width(90));GUI.color=p.estimatedSaveByte>0?CG:Color.gray;GUILayout.Label(p.estimatedSaveByte>0?"-"+FS(p.estimatedSaveByte):"-",GUILayout.Width(90));GUI.color=Color.white;}}
            });
        }

        // ===== フォント =====
        private void DrawFonts()
        {
            Pad(()=>{
                GUILayout.Space(6);if(!_analyzed){NA();return;}
                SL("フォント ("+_fonts.Count+" 件 / "+FS(_fonts.Sum(x=>x.sizeByte))+")");
                EditorGUILayout.HelpBox(
                    "【TTF/OTF】対応するSDFアセットがある場合、includeFontData=falseでビルドから除外できます。\n"+
                    "【SDF】アトラステクスチャをCrunch圧縮します（最大90%削減）。文字データは保持されるため文字欠けは発生しません。",
                    MessageType.Info);
                GUILayout.Space(4);
                SelButtons(()=>_fonts.ForEach(f=>f.selected=true),()=>_fonts.ForEach(f=>f.selected=false),()=>_fonts.ForEach(f=>f.selected=f.estimatedSaveByte>0));
                int n=_fonts.Count(f=>f.selected);GUI.enabled=n>0;
                if(GUILayout.Button(T("選択 ","Apply ")+n+T(" 件を適用"," selected"),_sPrim,GUILayout.Width(180)))
                    if(EditorUtility.DisplayDialog("確認",n+" 件のフォントを最適化します。\n（自動バックアップされます）","適用","キャンセル"))ApplyFonts();
                GUI.enabled=true;GUILayout.EndHorizontal();GUILayout.Space(4);
                                _searchFont=SearchBar(_searchFont);
using(new GUILayout.HorizontalScope(_sRow0)){GUILayout.Label("",GUILayout.Width(20));GUILayout.Label(T("ファイル名","File"),GUILayout.Width(300));GUILayout.Label("サイズ",GUILayout.Width(80));GUILayout.Label(T("種別","Type"),GUILayout.Width(55));GUILayout.Label(T("現状","Status"),GUILayout.Width(110));GUILayout.Label(T("問題点","Issues"),GUILayout.MinWidth(170));GUILayout.Label(T("削減見込み","Savings"),GUILayout.Width(90));}
                for(int i=0;i<_fonts.Count;i++){var f=_fonts[i];if(!MatchName(f.path,_searchFont))continue;using(new GUILayout.HorizontalScope(i%2==0?_sRow0:_sRow1)){f.selected=GUILayout.Toggle(f.selected,"",GUILayout.Width(20));GUILayout.Label(Path.GetFileName(f.path),GUILayout.Width(300));GUILayout.Label(FS(f.sizeByte),GUILayout.Width(80));GUILayout.Label(f.fontType,GUILayout.Width(55));GUI.color=Color.gray;if(f.fontType=="TTF")GUILayout.Label(f.includeFontData?T("データあり","Embedded"):T("データなし","Excluded"),GUILayout.Width(110));else GUILayout.Label(f.atlasWidth+"x"+f.atlasHeight+"px",GUILayout.Width(110));GUI.color=f.estimatedSaveByte>0?CY:CG;GUILayout.Label(!string.IsNullOrEmpty(f.issue)?f.issue:T("✓ 最適","✓ OK"),GUILayout.MinWidth(170));GUI.color=f.estimatedSaveByte>0?CG:Color.gray;GUILayout.Label(f.estimatedSaveByte>0?"-"+FS(f.estimatedSaveByte):"-",GUILayout.Width(90));GUI.color=Color.white;}}
            });
        }

        // ===== 不要アセット =====
        private void DrawCleanup()
        {
            Pad(()=>{
                GUILayout.Space(6);if(!_analyzed){NA();return;}
                SL(T("未使用アセット (","Unused Assets (")+_unused.Count+T(" 件 / "," files / ")+FS(_unused.Sum(u=>u.sizeByte))+")");
                EditorGUILayout.HelpBox("シーンから参照されていないアセットを検出します。\n削除したアセットは自動バックアップされ、バックアップタブから復元できます。",MessageType.Warning);
                GUILayout.Space(4);
                GUILayout.BeginHorizontal();
                if(GUILayout.Button(T("全選択","All"),_sBtn,GUILayout.Width(70)))_unused.ForEach(u=>u.selected=true);
                if(GUILayout.Button(T("全解除","None"),_sBtn,GUILayout.Width(70)))_unused.ForEach(u=>u.selected=false);
                GUILayout.FlexibleSpace();int n=_unused.Count(u=>u.selected);long sz=_unused.Where(u=>u.selected).Sum(u=>u.sizeByte);GUI.enabled=n>0;
                if(GUILayout.Button(T("選択 ","Delete ")+n+T(" 件を削除 ("," files (")+FS(sz)+")",_sDanger,GUILayout.Width(280)))
                    if(EditorUtility.DisplayDialog("警告",n+" 件のアセットを削除します。\n合計: "+FS(sz)+"\n\n削除前にバックアップが自動作成されます。","削除する","キャンセル"))DeleteUnused();
                GUI.enabled=true;GUILayout.EndHorizontal();GUILayout.Space(4);
                                _searchUnused=SearchBar(_searchUnused);
using(new GUILayout.HorizontalScope(_sRow0)){GUILayout.Label("",GUILayout.Width(20));GUILayout.Label(T("パス","Path"),GUILayout.MinWidth(380));GUILayout.Label(T("種別","Type"),GUILayout.Width(90));GUILayout.Label(T("サイズ","Size"),GUILayout.Width(90));}
                for(int i=0;i<_unused.Count;i++){var u=_unused[i];if(!MatchPath(u.path,_searchUnused))continue;using(new GUILayout.HorizontalScope(i%2==0?_sRow0:_sRow1)){u.selected=GUILayout.Toggle(u.selected,"",GUILayout.Width(20));GUILayout.Label(u.path,GUILayout.MinWidth(380));GUI.color=CY;GUILayout.Label(u.assetType,GUILayout.Width(90));GUI.color=Color.white;GUILayout.Label(FS(u.sizeByte),GUILayout.Width(90));}}
            });
        }

        // ===== バックアップ =====
        private void DrawBackup()
        {
            Pad(()=>{
                GUILayout.Space(6);SL(T("バックアップ / 復元 (","Backup / Restore (")+_backups.Count+" 件)");
                EditorGUILayout.HelpBox("最適化を適用する前に自動でバックアップを作成します。\n「復元」ボタンを押すと .meta と削除アセットを元に戻せます。",MessageType.Info);
                GUILayout.Space(4);
                GUILayout.BeginHorizontal();
                if(GUILayout.Button(T("一覧を更新","Refresh"),_sBtn,GUILayout.Width(120)))LoadBackupManifests();
                GUILayout.FlexibleSpace();
                if(GUILayout.Button(T("全バックアップを削除","Delete All"),_sBtn,GUILayout.Width(160)))
                    if(EditorUtility.DisplayDialog("確認","全てのバックアップを削除します。\n元に戻せなくなります。","削除","キャンセル"))DeleteAllBackups();
                GUILayout.EndHorizontal();GUILayout.Space(6);
                if(_backups.Count==0){EditorGUILayout.HelpBox("まだバックアップはありません。",MessageType.Warning);return;}
                using(new GUILayout.HorizontalScope(_sRow0)){GUILayout.Label(T("操作名","Label"),GUILayout.Width(200));GUILayout.Label(T("日時","Date"),GUILayout.Width(150));GUILayout.Label(".meta",GUILayout.Width(60));GUILayout.Label(T("削除件数","Deleted"),GUILayout.Width(60));GUILayout.Label(T("フォルダ","Folder"),GUILayout.MinWidth(100));GUILayout.Label("",GUILayout.Width(140));}
                _backupScroll=GUILayout.BeginScrollView(_backupScroll,GUILayout.MaxHeight(400));
                for(int i=_backups.Count-1;i>=0;i--){var b=_backups[i];using(new GUILayout.HorizontalScope(i%2==0?_sRow0:_sRow1)){GUILayout.Label(b.label,GUILayout.Width(200));GUILayout.Label(b.createdAt.ToString("MM/dd HH:mm:ss"),GUILayout.Width(150));GUILayout.Label(b.metaPaths.Count.ToString(),GUILayout.Width(60));GUILayout.Label(b.deletedOrigPaths.Count.ToString(),GUILayout.Width(60));GUILayout.Label(Path.GetFileName(b.backupDir),GUILayout.MinWidth(100));if(GUILayout.Button(T("復元","Restore"),_sRestore,GUILayout.Width(60))){string msg="「"+b.label+"」を元に戻しますか？\n"+b.metaPaths.Count+" 件の .meta を復元します。";if(b.deletedOrigPaths.Count>0)msg+="\n削除アセット "+b.deletedOrigPaths.Count+" 件も復元します。";if(EditorUtility.DisplayDialog("バックアップを復元",msg,"復元する","キャンセル"))RestoreBackup(b);}GUILayout.Space(4);if(GUILayout.Button("X",_sBtn,GUILayout.Width(26))){if(EditorUtility.DisplayDialog("確認","このバックアップを削除しますか？","削除","キャンセル")){DeleteBackup(b);break;}}}}
                GUILayout.EndScrollView();
            });
        }

        // ===== レポート =====
        private void DrawReport()
        {
            Pad(()=>{
                GUILayout.Space(6);SL(T("最適化レポート","Optimization Report"));if(!_analyzed){NA();return;}
                using(new GUILayout.VerticalScope(_sCard)){
                    long tt=_textures.Sum(x=>x.sizeByte),mt=_meshes.Sum(x=>x.sizeByte),at=_audios.Sum(x=>x.sizeByte),rt=_refProbes.Sum(x=>x.sizeByte),ft=_fonts.Sum(x=>x.sizeByte),ut=_unused.Sum(x=>x.sizeByte);
                    long ts=_textures.Sum(x=>x.estimatedSaveByte),ms=_meshes.Sum(x=>x.estimatedSaveByte),as2=_audios.Sum(x=>x.estimatedSaveByte),rs=_refProbes.Sum(x=>x.estimatedSaveByte),fs2=_fonts.Sum(x=>x.estimatedSaveByte);
                    GUILayout.Label(T("アセット別サマリー","Asset Summary"),_sSec);GUILayout.Space(4);
                    RR(T("テクスチャ合計","Textures Total"),tt,ts);RR(T("メッシュ合計","Meshes Total"),mt,ms);RR(T("オーディオ合計","Audio Total"),at,as2);RR(T("反射プローブ合計","Refl.Probe Total"),rt,rs);RR(T("フォント合計","Fonts Total"),ft,fs2);RR(T("未使用アセット合計","Unused Total"),ut,ut);
                    GUILayout.Space(4);EditorGUI.DrawRect(GUILayoutUtility.GetRect(0,1),new Color(.4f,.4f,.5f));GUILayout.Space(4);
                    GUILayout.BeginHorizontal();GUILayout.Label(T("合計削減見込み","Total Savings"),_sBold,GUILayout.Width(180));GUILayout.Label(T("約 ","~")+FS(ts+ms+as2+rs+fs2+ut)+T(" 削減可能"," can be reduced"),new GUIStyle(_sBold){fontSize=14,normal=new GUIStyleState{textColor=CG}});GUILayout.EndHorizontal();
                    GUILayout.Space(12);GUILayout.Label(T("問題のあるアセット","Issues Found"),_sSec);GUILayout.Space(4);
                    RI(T("テクスチャ 要最適化: ","Textures to optimize: ")+_textures.Count(t=>t.estimatedSaveByte>0)+T(" 件"," files"),_textures.Any(t=>t.estimatedSaveByte>0)?CY:CG);
                    RI(T("メッシュ 要最適化: ","Meshes to optimize: ")+_meshes.Count(m=>m.estimatedSaveByte>0)+T(" 件"," files"),_meshes.Any(m=>m.estimatedSaveByte>0)?CY:CG);
                    RI(T("オーディオ 要最適化: ","Audio to optimize: ")+_audios.Count(a=>a.estimatedSaveByte>0)+T(" 件"," files"),_audios.Any(a=>a.estimatedSaveByte>0)?CY:CG);
                    RI(T("反射プローブ 要最適化: ","Probes to optimize: ")+_refProbes.Count(p=>p.estimatedSaveByte>0)+T(" 件"," files"),_refProbes.Any(p=>p.estimatedSaveByte>0)?CY:CG);
                    RI(T("フォント 要最適化: ","Fonts to optimize: ")+_fonts.Count(f=>f.estimatedSaveByte>0)+T(" 件"," files"),_fonts.Any(f=>f.estimatedSaveByte>0)?CY:CG);
                    RI(T("未使用アセット: ","Unused assets: ")+_unused.Count+T(" 件"," files"),_unused.Count>0?CY:CG);
                    GUILayout.Space(10);if(GUILayout.Button(T("レポートを .txt に保存","Save Report as .txt"),_sBtn,GUILayout.Width(200)))SaveReport();
                }
            });
        }
        private void RR(string l,long t,long s){GUILayout.BeginHorizontal();GUILayout.Label(l,_sLabel,GUILayout.Width(200));GUILayout.Label(FS(t),GUILayout.Width(90));GUI.color=s>0?CG:Color.gray;GUILayout.Label(s>0?T("→ 約 ","-> ~")+FS(s)+T(" 削減"," saved"):T("変更なし","No change"),_sSmall);GUI.color=Color.white;GUILayout.EndHorizontal();}
        private void RI(string txt,Color col){GUI.color=col;GUILayout.Label("  - "+txt,_sSmall);GUI.color=Color.white;}

        // ===== 解析 =====
        private void AnalyzeProject()
        {
            _textures.Clear();_meshes.Clear();_audios.Clear();_unused.Clear();_refProbes.Clear();_fonts.Clear();
            _isProcessing=true;_progress=0f;
            try{
                var used=new HashSet<string>();CollectUsed(used);

                // ★ テクスチャ: Standalone オーバーライドも含めて問題判定
                var tg=AssetDatabase.FindAssets("t:Texture2D",new[]{"Assets"});
                for(int i=0;i<tg.Length;i++){
                    _progress=0.02f+0.2f*((float)i/Mathf.Max(tg.Length,1));
                    string p=AssetDatabase.GUIDToAssetPath(tg[i]);
                    var imp=AssetImporter.GetAtPath(p)as TextureImporter;if(imp==null)continue;
                    var tex=AssetDatabase.LoadAssetAtPath<Texture2D>(p);if(tex==null)continue;
                    long sz=FZ(p);long sv=0;
                    bool resOver=tex.width>_texMaxSize||tex.height>_texMaxSize;
                    var pcSet=imp.GetPlatformTextureSettings("Standalone");
                    bool pcOver=pcSet.overridden&&pcSet.maxTextureSize>_texMaxSize;
                    if(resOver||pcOver)sv+=(long)(sz*0.5f);
                    if(imp.isReadable&&_texDisableRW)sv+=(long)(sz*0.02f);
                    if(!imp.crunchedCompression&&_texEnableCrunch)sv+=(long)(sz*0.28f);
                    _textures.Add(new TextureInfo{texture=tex,path=p,sizeByte=sz,width=tex.width,height=tex.height,format=imp.GetDefaultPlatformTextureSettings().format,hasMipmap=imp.mipmapEnabled,isReadable=imp.isReadable,crunchCompressed=imp.crunchedCompression,usedInScene=used.Contains(p),selected=sv>0,estimatedSaveByte=sv});
                }

                // メッシュ
                var mg=AssetDatabase.FindAssets("t:Mesh",new[]{"Assets"});
                for(int i=0;i<mg.Length;i++){_progress=0.22f+0.15f*((float)i/Mathf.Max(mg.Length,1));string p=AssetDatabase.GUIDToAssetPath(mg[i]);var imp=AssetImporter.GetAtPath(p)as ModelImporter;if(imp==null)continue;var mesh=AssetDatabase.LoadAssetAtPath<Mesh>(p);if(mesh==null||_meshes.Any(m=>m.path==p))continue;long sz=FZ(p);long sv=0;if(imp.isReadable&&_meshDisableRW)sv+=(long)(sz*0.15f);if(imp.meshCompression==ModelImporterMeshCompression.Off)sv+=(long)(sz*0.2f);_meshes.Add(new MeshInfo{mesh=mesh,path=p,sizeByte=sz,isReadWrite=imp.isReadable,compression=imp.meshCompression,blendShapeCount=mesh.blendShapeCount,usedInScene=used.Contains(p),selected=sv>0,estimatedSaveByte=sv});}

                // オーディオ
                var ag=AssetDatabase.FindAssets("t:AudioClip",new[]{"Assets"});
                for(int i=0;i<ag.Length;i++){_progress=0.37f+0.12f*((float)i/Mathf.Max(ag.Length,1));string p=AssetDatabase.GUIDToAssetPath(ag[i]);var imp=AssetImporter.GetAtPath(p)as AudioImporter;if(imp==null)continue;var clip=AssetDatabase.LoadAssetAtPath<AudioClip>(p);if(clip==null)continue;long sz=FZ(p);var s=imp.defaultSampleSettings;long sv=s.compressionFormat==AudioCompressionFormat.PCM?(long)(sz*0.7f):0;_audios.Add(new AudioInfo{clip=clip,path=p,sizeByte=sz,format=s.compressionFormat,loadType=s.loadType,quality=s.quality,usedInScene=used.Contains(p),selected=sv>0,estimatedSaveByte=sv});}

                // 反射プローブ
                _progress=0.52f;
                var rpPaths=new HashSet<string>();
                foreach(var go in FindObjectsOfType<GameObject>()){foreach(var rp in go.GetComponentsInChildren<UnityEngine.ReflectionProbe>(true)){if(rp.bakedTexture!=null){var rpath=AssetDatabase.GetAssetPath(rp.bakedTexture);if(!string.IsNullOrEmpty(rpath))rpPaths.Add(rpath);}if(rp.customBakedTexture!=null){var rpath=AssetDatabase.GetAssetPath(rp.customBakedTexture);if(!string.IsNullOrEmpty(rpath))rpPaths.Add(rpath);}}}
                foreach(var guid in AssetDatabase.FindAssets("t:Cubemap",new[]{"Assets"}))rpPaths.Add(AssetDatabase.GUIDToAssetPath(guid));
                foreach(var p in rpPaths){if(string.IsNullOrEmpty(p)||!p.StartsWith("Assets"))continue;long sz=FZ(p);if(sz==0)continue;var tex=AssetDatabase.LoadAssetAtPath<Texture>(p);int res=tex!=null?tex.width:0;if(res<=0)continue;long sv=res>_probeTargetRes?(long)(sz*(1.0-(double)(_probeTargetRes*_probeTargetRes)/(double)(res*res)*0.9)):0;_refProbes.Add(new ReflectionProbeInfo{path=p,sizeByte=sz,currentRes=res,selected=sv>0,estimatedSaveByte=sv});}
                _refProbes=_refProbes.OrderByDescending(x=>x.sizeByte).ToList();

                // ★ フォント: TTF + SDF（TMP_FontAsset）
                _progress=0.67f;
                // TTF
                var fontGuids=AssetDatabase.FindAssets("t:Font",new[]{"Assets"});
                foreach(var guid in fontGuids){
                    string p=AssetDatabase.GUIDToAssetPath(guid);
                    var imp=AssetImporter.GetAtPath(p)as TrueTypeFontImporter;if(imp==null)continue;
                    long sz=FZ(p);
                    string dir=Path.GetDirectoryName(p),nameNoExt=Path.GetFileNameWithoutExtension(p);
                    bool hasSDF=AssetDatabase.FindAssets(nameNoExt+" SDF",new[]{dir}).Length>0||AssetDatabase.FindAssets(nameNoExt+" SDF",new[]{"Assets"}).Length>0;
                    string issue=hasSDF&&imp.includeFontData?"対応SDFあり → ビルド除外推奨":"";
                    long sv=(hasSDF&&imp.includeFontData)?(long)(sz*0.95f):0;
                    _fonts.Add(new FontInfo{path=p,sizeByte=sz,fontType="TTF",includeFontData=imp.includeFontData,selected=sv>0,estimatedSaveByte=sv,issue=issue});
                }
                // SDF (TMP_FontAsset)
                // ★ 修正: TextureImporterではなくTMP_FontAssetとして直接ロード
                var sdfGuids=AssetDatabase.FindAssets("SDF t:Object",new[]{"Assets"});
                foreach(var guid in sdfGuids){
                    string p=AssetDatabase.GUIDToAssetPath(guid);
                    if(!p.EndsWith(".asset"))continue;
                    var fa=AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(p);
                    if(fa==null)continue;
                    // TMP_FontAsset かどうかを型名で確認
                    if(fa.GetType().Name!="TMP_FontAsset")continue;
                    long sz=FZ(p);if(sz==0)continue;
                    // Reflection で atlasWidth/Height を取得
                    var t=fa.GetType();
                    int aw=(int)(t.GetProperty("atlasWidth")?.GetValue(fa)??0);
                    int ah=(int)(t.GetProperty("atlasHeight")?.GetValue(fa)??0);
                    // ★ SDF はアトラステクスチャを Crunch 圧縮（文字データは保持される安全な手法）
                    var atlasTex=AssetDatabase.LoadAllAssetsAtPath(p).OfType<Texture2D>().FirstOrDefault();
                    // 0x0 のアトラス（Dynamicモードで未生成）は圧縮不可
                    bool atlasEmpty=atlasTex==null||atlasTex.width<=0||atlasTex.height<=0;
                    bool alreadyCompressed=atlasTex!=null&&(atlasTex.format==TextureFormat.DXT5Crunched||atlasTex.format==TextureFormat.DXT1Crunched||atlasTex.format==TextureFormat.ETC2_RGBA8Crunched||atlasTex.format==TextureFormat.ETC_RGB4Crunched);
                    long sv=(atlasEmpty||alreadyCompressed)?0:(long)(sz*0.90); // 約90%削減見込み
                    string issue=atlasEmpty?T("未生成(Dynamic)で対象外","Empty atlas (Dynamic)"):(alreadyCompressed?T("圧縮済み","Already compressed"):T("アトラスCrunch圧縮で削減可能","Crunch compression available"));
                    _fonts.Add(new FontInfo{path=p,sizeByte=sz,fontType="SDF",atlasWidth=aw,atlasHeight=ah,selected=sv>0,estimatedSaveByte=sv,issue=issue});
                }
                _fonts=_fonts.OrderByDescending(x=>x.sizeByte).ToList();

                // 未使用アセット
                _progress=0.82f;
                foreach(var guid in AssetDatabase.FindAssets("t:Texture2D t:Mesh t:AudioClip",new[]{"Assets"})){string p=AssetDatabase.GUIDToAssetPath(guid);if(used.Contains(p)||_unused.Any(u=>u.path==p))continue;long sz=FZ(p);if(sz==0)continue;string ext=Path.GetExtension(p).ToLower();string type=IsTexExt(ext)?"Texture":IsMeshExt(ext)?"Mesh":IsAudExt(ext)?"Audio":"その他";_unused.Add(new UnusedAssetInfo{path=p,sizeByte=sz,assetType=type,selected=false});}

                _progress=1f;_analyzed=true;_tab=Tab.Dashboard;
            }finally{_isProcessing=false;EditorUtility.ClearProgressBar();Repaint();}
        }

        private void CollectUsed(HashSet<string> used)
        {
            foreach(var go in FindObjectsOfType<GameObject>()){
                foreach(var r in go.GetComponentsInChildren<Renderer>(true)){if(r.sharedMaterials==null)continue;foreach(var mat in r.sharedMaterials){if(mat==null)continue;used.Add(AssetDatabase.GetAssetPath(mat));if(mat.shader==null)continue;int cnt=ShaderUtil.GetPropertyCount(mat.shader);for(int p2=0;p2<cnt;p2++){if(ShaderUtil.GetPropertyType(mat.shader,p2)==ShaderUtil.ShaderPropertyType.TexEnv){var tex=mat.GetTexture(ShaderUtil.GetPropertyName(mat.shader,p2));if(tex!=null)used.Add(AssetDatabase.GetAssetPath(tex));}}}}
                foreach(var src in go.GetComponentsInChildren<AudioSource>(true))if(src.clip!=null)used.Add(AssetDatabase.GetAssetPath(src.clip));
                foreach(var mf in go.GetComponentsInChildren<MeshFilter>(true))if(mf.sharedMesh!=null)used.Add(AssetDatabase.GetAssetPath(mf.sharedMesh));
                foreach(var smr in go.GetComponentsInChildren<SkinnedMeshRenderer>(true))if(smr.sharedMesh!=null)used.Add(AssetDatabase.GetAssetPath(smr.sharedMesh));
                foreach(var rp in go.GetComponentsInChildren<UnityEngine.ReflectionProbe>(true)){if(rp.bakedTexture!=null)used.Add(AssetDatabase.GetAssetPath(rp.bakedTexture));if(rp.customBakedTexture!=null)used.Add(AssetDatabase.GetAssetPath(rp.customBakedTexture));}
            }
        }

        // ===== 適用処理 =====
        private void ApplyTextures(BackupEntry backup=null)
        {
            var tgts=_textures.Where(t=>t.selected).ToList();if(backup==null)backup=TakeBackup("テクスチャ最適化",tgts.Select(t=>t.path));
            for(int i=0;i<tgts.Count;i++){
                var t=tgts[i];EditorUtility.DisplayProgressBar("テクスチャ最適化",Path.GetFileName(t.path),(float)i/tgts.Count);
                var imp=AssetImporter.GetAtPath(t.path)as TextureImporter;if(imp==null)continue;
                bool ch=false;
                // デフォルト設定
                if(imp.maxTextureSize>_texMaxSize){imp.maxTextureSize=_texMaxSize;ch=true;}
                // ★ Standalone / Android のオーバーライドも上書き
                if(_texOverridePlatforms){
                    foreach(var platform in new[]{"Standalone","Android","iPhone"}){
                        var ps=imp.GetPlatformTextureSettings(platform);
                        if(ps.overridden&&ps.maxTextureSize>_texMaxSize){ps.maxTextureSize=_texMaxSize;imp.SetPlatformTextureSettings(ps);ch=true;}
                    }
                }
                if(_texDisableRW&&imp.isReadable){imp.isReadable=false;ch=true;}
                if(_iosCompatible){if(imp.crunchedCompression){imp.crunchedCompression=false;ch=true;}}else if(_texEnableCrunch&&!imp.crunchedCompression){imp.crunchedCompression=true;imp.compressionQuality=_texCrunchQuality;ch=true;}
                if(_texMipmapStream&&imp.mipmapEnabled&&!imp.streamingMipmaps){imp.streamingMipmaps=true;ch=true;}
                if(ch)imp.SaveAndReimport();
            }
            EditorUtility.ClearProgressBar();
            EditorApplication.delayCall+=()=>{AssetDatabase.Refresh();AnalyzeProject();
            EditorUtility.DisplayDialog(T("完了","Done"),tgts.Count+T(" 件のテクスチャを最適化しました。"," textures optimized."),"OK");};
        }

        private void ApplyMeshes(BackupEntry backup=null)
        {
            var tgts=_meshes.Where(m=>m.selected).ToList();if(backup==null)TakeBackup("メッシュ最適化",tgts.Select(m=>m.path));
            for(int i=0;i<tgts.Count;i++){var m=tgts[i];EditorUtility.DisplayProgressBar("メッシュ最適化",Path.GetFileName(m.path),(float)i/tgts.Count);var imp=AssetImporter.GetAtPath(m.path)as ModelImporter;if(imp==null)continue;bool ch=false;if(_meshDisableRW&&imp.isReadable){imp.isReadable=false;ch=true;}if(imp.meshCompression!=_meshCompression){imp.meshCompression=_meshCompression;ch=true;}if(ch)imp.SaveAndReimport();}
            EditorUtility.ClearProgressBar();
            EditorApplication.delayCall+=()=>{AssetDatabase.Refresh();AnalyzeProject();
            EditorUtility.DisplayDialog(T("完了","Done"),tgts.Count+T(" 件のメッシュを最適化しました。"," meshes optimized."),"OK");};
        }

        private void ApplyAudios(BackupEntry backup=null)
        {
            var tgts=_audios.Where(a=>a.selected).ToList();if(backup==null)TakeBackup("オーディオ最適化",tgts.Select(a=>a.path));
            for(int i=0;i<tgts.Count;i++){var a=tgts[i];EditorUtility.DisplayProgressBar("オーディオ最適化",Path.GetFileName(a.path),(float)i/tgts.Count);var imp=AssetImporter.GetAtPath(a.path)as AudioImporter;if(imp==null)continue;var s=imp.defaultSampleSettings;s.compressionFormat=_audioFormat;s.loadType=_audioLoadType;s.quality=_audioQuality;imp.defaultSampleSettings=s;imp.SaveAndReimport();}
            EditorUtility.ClearProgressBar();
            EditorApplication.delayCall+=()=>{AssetDatabase.Refresh();AnalyzeProject();
            EditorUtility.DisplayDialog(T("完了","Done"),tgts.Count+T(" 件のオーディオを最適化しました。"," audio clips optimized."),"OK");};
        }

        private void ApplyReflProbes(BackupEntry backup=null)
        {
            var tgts=_refProbes.Where(p=>p.selected).ToList();
            if(backup==null)backup=TakeBackup("反射プローブ最適化",tgts.Select(p=>p.path));
            string _pr=Application.dataPath.Replace("/Assets","");
            int done=0;
            for(int i=0;i<tgts.Count;i++){
                var p=tgts[i];
                EditorUtility.DisplayProgressBar(T("反射プローブ最適化","Optimizing Probes"),Path.GetFileName(p.path),(float)i/tgts.Count);
                if(p.currentRes<=_probeTargetRes)continue;
                // ★ 本体ファイル(.exr/.png等)をバックアップ（.metaだけでは画質は戻らないため）
                string _srcFull=Path.Combine(_pr,p.path);
                if(File.Exists(_srcFull)){
                    string _relPath=p.path.StartsWith("Assets/")?p.path.Substring(7):p.path.Replace("Assets\\","");
                    string _dstBody=Path.Combine(backup.backupDir,"body",_relPath);
                    Directory.CreateDirectory(Path.GetDirectoryName(_dstBody));
                    File.Copy(_srcFull,_dstBody,true);
                    backup.assetBodyOrigPaths.Add(p.path);
                    backup.assetBodyBackPaths.Add(_dstBody);
                }
                var imp=AssetImporter.GetAtPath(p.path)as TextureImporter;
                if(imp==null)continue;
                imp.maxTextureSize=_probeTargetRes;
                if(_texOverridePlatforms){
                    foreach(var platform in new[]{"Standalone","Android","iPhone"}){
                        var ps=imp.GetPlatformTextureSettings(platform);
                        if(ps.overridden&&ps.maxTextureSize>_probeTargetRes){ps.maxTextureSize=_probeTargetRes;imp.SetPlatformTextureSettings(ps);}
                    }
                }
                imp.SaveAndReimport();done++;
            }
            SaveManifest(backup); // 本体バックアップのパスを保存
            EditorUtility.ClearProgressBar();
            EditorApplication.delayCall+=()=>{AssetDatabase.Refresh();AnalyzeProject();
            EditorUtility.DisplayDialog(T("完了","Done"),done+T(" 件の反射プローブを"," probe(s) reduced to ")+_probeTargetRes+T("pxに削減しました。\n見た目に問題があればバックアップタブから復元してください。","px.\nCheck visuals and restore from Backup tab if needed."),"OK");};
        }

        private void ApplyFonts(BackupEntry backup=null)
        {
            var tgts=_fonts.Where(f=>f.selected).ToList();if(backup==null)backup=TakeBackup("フォント最適化",tgts.Select(f=>f.path));
            int done=0;
            for(int i=0;i<tgts.Count;i++){
                var f=tgts[i];EditorUtility.DisplayProgressBar("フォント最適化",Path.GetFileName(f.path),(float)i/tgts.Count);
                if(f.fontType=="TTF"){
                    var imp=AssetImporter.GetAtPath(f.path)as TrueTypeFontImporter;
                    if(imp!=null&&imp.includeFontData){imp.includeFontData=false;imp.SaveAndReimport();done++;}
                }else if(f.fontType=="SDF"){if(_iosCompatible)continue; // iOS版対応: SDFアトラスのCrunch圧縮(iPhone非対応)をスキップ 
                    // ★ アトラステクスチャを Crunch 圧縮（文字データは保持）
                    // 本体バックアップ（失敗時に確実に戻せるよう必須）
                    string _pRoot=Application.dataPath.Replace("/Assets","");
                    string _srcFull=Path.Combine(_pRoot,f.path);
                    if(!File.Exists(_srcFull))continue;
                    string _relPath=f.path.StartsWith("Assets/")?f.path.Substring(7):f.path.Replace("Assets\\","");
                    string _dstBody=Path.Combine(backup.backupDir,"body",_relPath);
                    Directory.CreateDirectory(Path.GetDirectoryName(_dstBody));
                    File.Copy(_srcFull,_dstBody,true);
                    backup.assetBodyOrigPaths.Add(f.path);
                    backup.assetBodyBackPaths.Add(_dstBody);
                    // アトラステクスチャ(Texture2Dサブアセット)を取得して圧縮
                    var atlasTex=AssetDatabase.LoadAllAssetsAtPath(f.path).OfType<Texture2D>().FirstOrDefault();
                    // 0x0 のアトラス（Dynamicモードで未生成）はスキップ
                    if(atlasTex==null||atlasTex.width<=0||atlasTex.height<=0){
                        // バックアップは取ったが圧縮しないので削除しておく
                        if(backup.assetBodyOrigPaths.Count>0&&backup.assetBodyOrigPaths[backup.assetBodyOrigPaths.Count-1]==f.path){
                            try{File.Delete(_dstBody);}catch{}
                            backup.assetBodyOrigPaths.RemoveAt(backup.assetBodyOrigPaths.Count-1);
                            backup.assetBodyBackPaths.RemoveAt(backup.assetBodyBackPaths.Count-1);
                        }
                        continue;
                    }
                    bool isCompressed=atlasTex.format==TextureFormat.DXT5Crunched||atlasTex.format==TextureFormat.DXT1Crunched||atlasTex.format==TextureFormat.ETC2_RGBA8Crunched||atlasTex.format==TextureFormat.ETC_RGB4Crunched;
                    if(isCompressed)continue; // 既に圧縮済みはスキップ
                    try{
                        EditorUtility.CompressTexture(atlasTex,TextureFormat.DXT5Crunched,50);
                        EditorUtility.SetDirty(atlasTex);
                        done++;
                    }catch(System.Exception ex){
                        Debug.LogError("SDF圧縮失敗 ("+f.path+"): "+ex.Message);
                        // 失敗時は本体バックアップから即時復元
                        File.Copy(_dstBody,_srcFull,true);
                    }
                }
            }
            AssetDatabase.SaveAssets();
            SaveManifest(backup); // ★ SDF本体バックアップのパスをmanifestに保存
            EditorUtility.ClearProgressBar();
            EditorApplication.delayCall+=()=>{AssetDatabase.Refresh();AnalyzeProject();
            EditorUtility.DisplayDialog(T("完了","Done"),done+T(" 件のフォントを最適化しました。\n※ SDF フォントは次回ビルド時にアトラスが再生成されます。"," font(s) optimized.\n* SDF fonts will be regenerated on next build."),"OK");};
        }

        private void DeleteUnused()
        {
            var tgts=_unused.Where(u=>u.selected).ToList();var backup=TakeBackup("未使用アセット削除",tgts.Select(u=>u.path),true);
            int del=0;string root=Application.dataPath.Replace("/Assets","");
            for(int i=0;i<tgts.Count;i++){var u=tgts[i];EditorUtility.DisplayProgressBar("未使用アセット削除",u.path,(float)i/tgts.Count);string src=Path.Combine(root,u.path),meta=src+".meta",rel=u.path.StartsWith("Assets/")?u.path.Substring(7):u.path.Replace("Assets\\",""),dst=Path.Combine(backup.backupDir,"deleted",rel);Directory.CreateDirectory(Path.GetDirectoryName(dst));if(File.Exists(src)){File.Copy(src,dst,true);backup.deletedOrigPaths.Add(u.path);backup.deletedBackPaths.Add(dst);}if(File.Exists(meta))File.Copy(meta,dst+".meta",true);if(AssetDatabase.DeleteAsset(u.path))del++;}
            SaveManifest(backup);EditorUtility.ClearProgressBar();
            EditorApplication.delayCall+=()=>{AssetDatabase.Refresh();AnalyzeProject();EditorUtility.DisplayDialog(T("完了","Done"),del+T(" 件を削除しました。\nバックアップタブから復元できます。"," asset(s) deleted.\nRestore from the Backup tab if needed."),"OK");};
        }

        private void ApplyAll()
        {
            var allPaths=_textures.Where(t=>t.selected).Select(t=>t.path).Concat(_meshes.Where(m=>m.selected).Select(m=>m.path)).Concat(_audios.Where(a=>a.selected).Select(a=>a.path)).Concat(_refProbes.Where(p=>p.selected).Select(p=>p.path)).Concat(_fonts.Where(f=>f.selected).Select(f=>f.path));
            var backup=TakeBackup("全項目一括適用",allPaths);
            ApplyTextures(backup);ApplyMeshes(backup);ApplyAudios(backup);ApplyReflProbes(backup);ApplyFonts(backup);
            EditorUtility.DisplayDialog("全最適化完了","全ての最適化を適用しました。\nバックアップタブからいつでも元に戻せます。","OK");
        }

        // ===== シーン使用中のアセットのみに適用 =====
        private void ApplySceneOnly()
        {
            var texScene   = _textures.Where(t=>t.selected&&t.usedInScene).ToList();
            var meshScene  = _meshes.Where(m=>m.selected&&m.usedInScene).ToList();
            var audioScene = _audios.Where(a=>a.selected&&a.usedInScene).ToList();
            var probeScene = _refProbes.Where(p=>p.selected).ToList();
            var fontScene  = _fonts.Where(f=>f.selected).ToList();
            int total=texScene.Count+meshScene.Count+audioScene.Count+probeScene.Count+fontScene.Count;
            if(total==0){
                EditorUtility.DisplayDialog(T("対象なし","Nothing to Apply"),
                    T("シーンで使用中かつ最適化対象のアセットがありません。\n先に解析を実行してください。","No scene-used assets need optimization.\nPlease run Analyze first."),"OK");
                return;
            }
            var allPaths=texScene.Select(t=>t.path).Concat(meshScene.Select(m=>m.path))
                .Concat(audioScene.Select(a=>a.path)).Concat(probeScene.Select(p=>p.path)).Concat(fontScene.Select(f=>f.path));
            var backup=TakeBackup(T("シーン使用中のみ適用","Scene-Used Only"),allPaths);
            string _pr=Application.dataPath.Replace("/Assets","");
            // テクスチャ
            foreach(var tx in texScene){
                var imp2=AssetImporter.GetAtPath(tx.path)as TextureImporter;if(imp2==null)continue;
                bool ch2=false;
                if(imp2.maxTextureSize>_texMaxSize){imp2.maxTextureSize=_texMaxSize;ch2=true;}
                if(_texOverridePlatforms){foreach(var pl in new[]{"Standalone","Android","iPhone"}){var ps2=imp2.GetPlatformTextureSettings(pl);if(ps2.overridden&&ps2.maxTextureSize>_texMaxSize){ps2.maxTextureSize=_texMaxSize;imp2.SetPlatformTextureSettings(ps2);ch2=true;}}}
                if(_texDisableRW&&imp2.isReadable){imp2.isReadable=false;ch2=true;}
                if(_iosCompatible){if(imp2.crunchedCompression){imp2.crunchedCompression=false;ch2=true;}}else if(_texEnableCrunch&&!imp2.crunchedCompression){imp2.crunchedCompression=true;imp2.compressionQuality=_texCrunchQuality;ch2=true;}
                if(_texMipmapStream&&imp2.mipmapEnabled&&!imp2.streamingMipmaps){imp2.streamingMipmaps=true;ch2=true;}
                if(ch2)imp2.SaveAndReimport();
            }
            // メッシュ
            foreach(var mx in meshScene){
                var imp2=AssetImporter.GetAtPath(mx.path)as ModelImporter;if(imp2==null)continue;
                bool ch2=false;
                if(_meshDisableRW&&imp2.isReadable){imp2.isReadable=false;ch2=true;}
                if(imp2.meshCompression!=_meshCompression){imp2.meshCompression=_meshCompression;ch2=true;}
                if(ch2)imp2.SaveAndReimport();
            }
            // オーディオ
            foreach(var ax in audioScene){
                var imp2=AssetImporter.GetAtPath(ax.path)as AudioImporter;if(imp2==null)continue;
                var s2=imp2.defaultSampleSettings;s2.compressionFormat=_audioFormat;s2.loadType=_audioLoadType;s2.quality=_audioQuality;
                imp2.defaultSampleSettings=s2;imp2.SaveAndReimport();
            }
            // ReflectionProbe (本体.exrをバックアップ)
            foreach(var px in probeScene){
                if(px.currentRes<=_probeTargetRes)continue;
                string _pxSrc=Path.Combine(_pr,px.path);
                if(File.Exists(_pxSrc)){
                    string _pxRel=px.path.StartsWith("Assets/")?px.path.Substring(7):px.path.Replace("Assets\\","");
                    string _pxDst=Path.Combine(backup.backupDir,"body",_pxRel);
                    Directory.CreateDirectory(Path.GetDirectoryName(_pxDst));
                    File.Copy(_pxSrc,_pxDst,true);
                    backup.assetBodyOrigPaths.Add(px.path);
                    backup.assetBodyBackPaths.Add(_pxDst);
                }
                var imp2=AssetImporter.GetAtPath(px.path)as TextureImporter;if(imp2==null)continue;
                imp2.maxTextureSize=_probeTargetRes;
                if(_texOverridePlatforms){foreach(var pl in new[]{"Standalone","Android","iPhone"}){var ps2=imp2.GetPlatformTextureSettings(pl);if(ps2.overridden&&ps2.maxTextureSize>_probeTargetRes){ps2.maxTextureSize=_probeTargetRes;imp2.SetPlatformTextureSettings(ps2);}}}
                imp2.SaveAndReimport();
            }
            // フォント TTF: ビルドから除外 / SDF: アトラスCrunch圧縮（文字データ保持）
            foreach(var fx in fontScene){
                if(fx.fontType=="TTF"){
                    var imp2=AssetImporter.GetAtPath(fx.path)as TrueTypeFontImporter;
                    if(imp2!=null&&imp2.includeFontData){imp2.includeFontData=false;imp2.SaveAndReimport();}
                }else if(fx.fontType=="SDF"){if(_iosCompatible)continue; // iOS対応: SDFアトラスのCrunch圧縮(iPhone非対応)をスキップ 
                    // 本体バックアップ
                    string _sf=Path.Combine(_pr,fx.path);if(!File.Exists(_sf))continue;
                    string _rp2=fx.path.StartsWith("Assets/")?fx.path.Substring(7):fx.path.Replace("Assets\\","");
                    string _dp=Path.Combine(backup.backupDir,"body",_rp2);
                    Directory.CreateDirectory(Path.GetDirectoryName(_dp));File.Copy(_sf,_dp,true);
                    backup.assetBodyOrigPaths.Add(fx.path);backup.assetBodyBackPaths.Add(_dp);
                    // アトラステクスチャ圧縮
                    var atlasTex2=AssetDatabase.LoadAllAssetsAtPath(fx.path).OfType<Texture2D>().FirstOrDefault();
                    if(atlasTex2==null||atlasTex2.width<=0||atlasTex2.height<=0){
                        // Dynamic等で未生成なアトラス → バックアップを取り消してスキップ
                        if(backup.assetBodyOrigPaths.Count>0&&backup.assetBodyOrigPaths[backup.assetBodyOrigPaths.Count-1]==fx.path){
                            try{File.Delete(_dp);}catch{}
                            backup.assetBodyOrigPaths.RemoveAt(backup.assetBodyOrigPaths.Count-1);
                            backup.assetBodyBackPaths.RemoveAt(backup.assetBodyBackPaths.Count-1);
                        }
                        continue;
                    }
                    bool isCompressed2=atlasTex2.format==TextureFormat.DXT5Crunched||atlasTex2.format==TextureFormat.DXT1Crunched||atlasTex2.format==TextureFormat.ETC2_RGBA8Crunched||atlasTex2.format==TextureFormat.ETC_RGB4Crunched;
                    if(isCompressed2)continue;
                    try{
                        EditorUtility.CompressTexture(atlasTex2,TextureFormat.DXT5Crunched,50);
                        EditorUtility.SetDirty(atlasTex2);
                    }catch(System.Exception ex){
                        Debug.LogError("SDF圧縮失敗 ("+fx.path+"): "+ex.Message);
                        File.Copy(_dp,_sf,true);
                    }
                }
            }
            AssetDatabase.SaveAssets();SaveManifest(backup);
            int _tc=texScene.Count,_mc=meshScene.Count,_ac=audioScene.Count;
            EditorApplication.delayCall+=()=>{AssetDatabase.Refresh();AnalyzeProject();
            EditorUtility.DisplayDialog(
                T("シーン使用中のみ 完了","Scene-Used Apply Done"),
                T("シーンで使用中のアセットのみ最適化を適用しました。","Optimized scene-used assets only.")+"\n"+
                T("テクスチャ: ","Textures: ")+_tc+T(" 件 / メッシュ: "," / Meshes: ")+_mc+
                T(" 件 / オーディオ: "," / Audio: ")+_ac+T(" 件"," files")+"\n"+
                T("バックアップタブからいつでも元に戻せます。","Restore anytime from the Backup tab."),
                "OK");};
        }

        // ===== バックアップロジック =====
        private BackupEntry TakeBackup(string label,IEnumerable<string> paths=null,bool isDel=false)
        {
            string id=DateTime.Now.ToString("yyyyMMdd_HHmmss"),dir=Path.Combine(Application.dataPath.Replace("/Assets",""),BACKUP_ROOT,id);
            Directory.CreateDirectory(dir);var e=new BackupEntry{id=id,label=label,createdAt=DateTime.Now,backupDir=dir};
            if(paths!=null)foreach(var p in paths){string src=Path.Combine(Application.dataPath.Replace("/Assets",""),p+".meta");if(!File.Exists(src))continue;string rel=p.StartsWith("Assets/")?p.Substring(7):p.Replace("Assets\\",""),dst=Path.Combine(dir,"meta",rel+".meta");Directory.CreateDirectory(Path.GetDirectoryName(dst));File.Copy(src,dst,true);e.metaPaths.Add(p);}
            if(!isDel)SaveManifest(e);_backups.Add(e);return e;
        }
        private void SaveManifest(BackupEntry e){var sb=new System.Text.StringBuilder();sb.AppendLine(e.id);sb.AppendLine(e.label);sb.AppendLine(e.createdAt.ToString("o"));sb.AppendLine("META:"+string.Join("|",e.metaPaths));sb.AppendLine("ORIG:"+string.Join("|",e.deletedOrigPaths));sb.AppendLine("BACK:"+string.Join("|",e.deletedBackPaths));sb.AppendLine("BODYORIG:"+string.Join("|",e.assetBodyOrigPaths));sb.AppendLine("BODYBACK:"+string.Join("|",e.assetBodyBackPaths));File.WriteAllText(Path.Combine(e.backupDir,"manifest.txt"),sb.ToString(),System.Text.Encoding.UTF8);}
        private void LoadBackupManifests()
        {
            _backups.Clear();string root=Path.Combine(Application.dataPath.Replace("/Assets",""),BACKUP_ROOT);if(!Directory.Exists(root))return;
            foreach(var dir in Directory.GetDirectories(root).OrderBy(d=>d)){string mf=Path.Combine(dir,"manifest.txt");if(!File.Exists(mf))continue;var lines=File.ReadAllLines(mf,System.Text.Encoding.UTF8);if(lines.Length<6)continue;try{var e2=new BackupEntry{id=lines[0].Trim(),label=lines[1].Trim(),createdAt=DateTime.Parse(lines[2].Trim()),backupDir=dir};string ml=lines[3].Replace("META:","").Trim(),ol=lines[4].Replace("ORIG:","").Trim(),bl=lines[5].Replace("BACK:","").Trim();if(!string.IsNullOrEmpty(ml))e2.metaPaths=ml.Split('|').Where(s=>!string.IsNullOrEmpty(s)).ToList();if(!string.IsNullOrEmpty(ol))e2.deletedOrigPaths=ol.Split('|').Where(s=>!string.IsNullOrEmpty(s)).ToList();if(!string.IsNullOrEmpty(bl))e2.deletedBackPaths=bl.Split('|').Where(s=>!string.IsNullOrEmpty(s)).ToList();if(lines.Length>=8){string boL=lines[6].Replace("BODYORIG:","").Trim(),bbL=lines[7].Replace("BODYBACK:","").Trim();if(!string.IsNullOrEmpty(boL))e2.assetBodyOrigPaths=boL.Split('|').Where(s=>!string.IsNullOrEmpty(s)).ToList();if(!string.IsNullOrEmpty(bbL))e2.assetBodyBackPaths=bbL.Split('|').Where(s=>!string.IsNullOrEmpty(s)).ToList();}_backups.Add(e2);}catch{}}
            Repaint();
        }
        private void RestoreBackup(BackupEntry e)
        {
            int r=0;string root=Application.dataPath.Replace("/Assets","");EditorUtility.DisplayProgressBar("バックアップ復元","metaファイルを復元中...",0f);
            for(int i=0;i<e.metaPaths.Count;i++){string p=e.metaPaths[i],rel=p.StartsWith("Assets/")?p.Substring(7):p.Replace("Assets\\",""),src=Path.Combine(e.backupDir,"meta",rel+".meta"),dst=Path.Combine(root,p+".meta");if(!File.Exists(src))continue;Directory.CreateDirectory(Path.GetDirectoryName(dst));File.Copy(src,dst,true);r++;EditorUtility.DisplayProgressBar("バックアップ復元",Path.GetFileName(p),(float)i/e.metaPaths.Count);}
            for(int i=0;i<e.deletedOrigPaths.Count;i++){string op=e.deletedOrigPaths[i],bp=e.deletedBackPaths[i],df=Path.Combine(root,op);if(!File.Exists(bp))continue;Directory.CreateDirectory(Path.GetDirectoryName(df));File.Copy(bp,df,true);if(File.Exists(bp+".meta"))File.Copy(bp+".meta",df+".meta",true);r++;}
            // アセット本体(.asset 等)の復元 ★ SDF バグ修正
            for(int ri=0;ri<e.assetBodyOrigPaths.Count;ri++){
                string _origP=e.assetBodyOrigPaths[ri],_backP=e.assetBodyBackPaths[ri];
                string _dstF=Path.Combine(root,_origP);
                if(!File.Exists(_backP))continue;
                Directory.CreateDirectory(Path.GetDirectoryName(_dstF));
                File.Copy(_backP,_dstF,true);
                r++;
                EditorUtility.DisplayProgressBar(T("バックアップ復元","Restoring"),Path.GetFileName(_origP),0.9f);
            }
            EditorUtility.ClearProgressBar();
            EditorApplication.delayCall+=()=>{
                AssetDatabase.Refresh();
                AnalyzeProject();
                EditorUtility.DisplayDialog(T("復元完了","Restore Complete"),"「"+e.label+"」"+T("を復元しました。\n"," restored.\n")+r+T(" 件のファイルを元に戻しました。"," file(s) reverted."),"OK");
            };
        }
        private void DeleteBackup(BackupEntry e){if(Directory.Exists(e.backupDir))Directory.Delete(e.backupDir,true);_backups.Remove(e);Repaint();}
        private void DeleteAllBackups(){string root=Path.Combine(Application.dataPath.Replace("/Assets",""),BACKUP_ROOT);if(Directory.Exists(root))Directory.Delete(root,true);_backups.Clear();Repaint();}

        // ===== レポート保存 =====
        private void SaveReport()
        {
            string sp=EditorUtility.SaveFilePanel("レポート保存","","VRCOptimizeReport","txt");if(string.IsNullOrEmpty(sp))return;
            var sb=new System.Text.StringBuilder();
            sb.AppendLine("============================================");sb.AppendLine("  VRC World Build Optimizer レポート");sb.AppendLine("  生成日時: "+System.DateTime.Now.ToString("yyyy/MM/dd HH:mm:ss"));sb.AppendLine("============================================\n");
            sb.AppendLine("[テクスチャ] "+_textures.Count+" 件  合計: "+FS(_textures.Sum(t=>t.sizeByte))+"  削減見込み: "+FS(_textures.Sum(t=>t.estimatedSaveByte)));foreach(var t in _textures.Where(t=>t.estimatedSaveByte>0))sb.AppendLine("  - "+t.path+"  ("+t.width+"x"+t.height+")  問題: "+string.Join(", ",TexIssues(t))+"  削減: "+FS(t.estimatedSaveByte));
            sb.AppendLine("\n[メッシュ] "+_meshes.Count+" 件  合計: "+FS(_meshes.Sum(m=>m.sizeByte))+"  削減見込み: "+FS(_meshes.Sum(m=>m.estimatedSaveByte)));foreach(var m in _meshes.Where(m=>m.estimatedSaveByte>0))sb.AppendLine("  - "+m.path+"  R/W:"+m.isReadWrite+"  圧縮:"+m.compression+"  削減: "+FS(m.estimatedSaveByte));
            sb.AppendLine("\n[オーディオ] "+_audios.Count+" 件  合計: "+FS(_audios.Sum(a=>a.sizeByte))+"  削減見込み: "+FS(_audios.Sum(a=>a.estimatedSaveByte)));foreach(var a in _audios.Where(a=>a.estimatedSaveByte>0))sb.AppendLine("  - "+a.path+"  形式:"+a.format+"  削減: "+FS(a.estimatedSaveByte));
            sb.AppendLine("\n[反射プローブ] "+_refProbes.Count+" 件  合計: "+FS(_refProbes.Sum(p=>p.sizeByte))+"  削減見込み: "+FS(_refProbes.Sum(p=>p.estimatedSaveByte)));foreach(var p in _refProbes.Where(p=>p.estimatedSaveByte>0))sb.AppendLine("  - "+p.path+"  現在:"+p.currentRes+"px → 目標:"+_probeTargetRes+"px  削減: "+FS(p.estimatedSaveByte));
            sb.AppendLine("\n[フォント] "+_fonts.Count+" 件  合計: "+FS(_fonts.Sum(f=>f.sizeByte))+"  削減見込み: "+FS(_fonts.Sum(f=>f.estimatedSaveByte)));foreach(var f in _fonts.Where(f=>f.estimatedSaveByte>0))sb.AppendLine("  - "+f.path+"  ("+f.fontType+")  問題: "+f.issue+"  削減: "+FS(f.estimatedSaveByte));
            sb.AppendLine("\n[未使用] "+_unused.Count+" 件  合計: "+FS(_unused.Sum(u=>u.sizeByte)));foreach(var u in _unused)sb.AppendLine("  - "+u.path+"  ("+u.assetType+")  "+FS(u.sizeByte));
            long total=_textures.Sum(t=>t.estimatedSaveByte)+_meshes.Sum(m=>m.estimatedSaveByte)+_audios.Sum(a=>a.estimatedSaveByte)+_refProbes.Sum(p=>p.estimatedSaveByte)+_fonts.Sum(f=>f.estimatedSaveByte)+_unused.Sum(u=>u.sizeByte);
            sb.AppendLine("\n合計削減見込み: "+FS(total));
            File.WriteAllText(sp,sb.ToString(),System.Text.Encoding.UTF8);EditorUtility.DisplayDialog("保存完了","レポートを保存しました:\n"+sp,"OK");
        }

        // ===== ユーティリティ =====
        private void SelButtons(Action all,Action none,Action rec){GUILayout.BeginHorizontal();if(GUILayout.Button(T("全選択","All"),_sBtn,GUILayout.Width(70)))all();if(GUILayout.Button(T("全解除","None"),_sBtn,GUILayout.Width(70)))none();if(GUILayout.Button(T("推奨のみ","Recommended"),_sBtn,GUILayout.Width(80)))rec();GUILayout.FlexibleSpace();}
        private static long   FZ(string p){var fi=new FileInfo(Path.Combine(Application.dataPath.Replace("/Assets",""),p));return fi.Exists?fi.Length:0;}
        private static string FS(long b){if(b<=0)return "0 B";if(b<1024)return b+" B";if(b<1024*1024)return (b/1024.0).ToString("F1")+" KB";return (b/(1024.0*1024)).ToString("F2")+" MB";}
        private static bool IsTexExt(string e)=>e==".png"||e==".jpg"||e==".jpeg"||e==".tga"||e==".psd"||e==".bmp"||e==".exr"||e==".hdr";
        private static bool IsMeshExt(string e)=>e==".fbx"||e==".obj"||e==".blend"||e==".dae"||e==".3ds";
        private static bool IsAudExt(string e)=>e==".wav"||e==".mp3"||e==".ogg"||e==".aiff"||e==".aif";
        private void SL(string t){GUILayout.Space(4);GUILayout.Label(t,_sSec);EditorGUI.DrawRect(GUILayoutUtility.GetRect(0,1),new Color(.35f,.35f,.5f));GUILayout.Space(4);}
        private void NA(){GUILayout.Space(16);EditorGUILayout.HelpBox(T("「ダッシュボード」タブの「プロジェクト全体を解析」ボタンを押して解析を実行してください。","Click 'Analyze Project' on the Dashboard tab."),MessageType.Info);}
        private void Pad(System.Action draw){GUILayout.BeginHorizontal();GUILayout.Space(12);GUILayout.BeginVertical();draw();GUILayout.Space(12);GUILayout.EndVertical();GUILayout.Space(12);GUILayout.EndHorizontal();}
    }
}
