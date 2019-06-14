﻿using Barotrauma.Extensions;
using Barotrauma.Networking;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using OpenAL;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace Barotrauma
{
    public partial class GameSettings
    {
        public enum Tab
        {
            Graphics,
            Audio,
            Controls,
        }

        private readonly Point MinSupportedResolution = new Point(1024, 540);

        private GUIFrame settingsFrame;
        private GUIButton applyButton;

        private GUIFrame[] tabs;
        private GUIButton[] tabButtons;

        public Action OnHUDScaleChanged;

        public GUIFrame SettingsFrame
        {
            get
            {
                if (settingsFrame == null) CreateSettingsFrame();
                return settingsFrame;
            }
        }

        private bool ChangeSliderText(GUIScrollBar scrollBar, float barScroll)
        {
            UnsavedSettings = true;
            GUITextBlock text = scrollBar.UserData as GUITextBlock;
            //search for percentage value
            int index = text.Text.IndexOf("%");
            string label = text.Text;
            //if "%" is found
            if (index > 0)
            {
                while (index > 0)
                {
                    //search for end of label
                    index -= 1;
                    if (text.Text[index] == ' ')
                        break;
                }
                label = text.Text.Substring(0, index);
            }
            text.Text = label + " " + barScroll * 100 + "%";
            return true;
        }

        public void ResetSettingsFrame()
        {
            if (GameMain.Client == null)
            {
                VoipCapture.Instance?.Dispose();
            }
            settingsFrame = null;
        }

        public void CreateSettingsFrame(Tab selectedTab = Tab.Graphics)
        {
            settingsFrame = new GUIFrame(new RectTransform(new Vector2(0.8f, 0.8f), GUI.Canvas, Anchor.Center));

            Vector2 tickBoxScale = Vector2.One * 0.05f;

            var settingsFramePadding = new GUILayoutGroup(new RectTransform(new Vector2(0.95f, 0.9f), settingsFrame.RectTransform, Anchor.TopCenter) { RelativeOffset = new Vector2(0.0f, 0.05f) }) { RelativeSpacing = 0.01f, IsHorizontal = true };

            /// General tab --------------------------------------------------------------

            var leftPanel = new GUILayoutGroup(new RectTransform(new Vector2(0.25f, 1.0f), settingsFramePadding.RectTransform, Anchor.TopLeft));

            var settingsTitle = new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.05f), leftPanel.RectTransform),
                TextManager.Get("Settings"), textAlignment: Alignment.TopLeft, font: GUI.LargeFont)
            { ForceUpperCase = true };

            var generalLayoutGroup = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 1.0f), leftPanel.RectTransform, Anchor.TopLeft));

            new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.05f), generalLayoutGroup.RectTransform), TextManager.Get("ContentPackages"));
            var contentPackageList = new GUIListBox(new RectTransform(new Vector2(1.0f, 0.75f), generalLayoutGroup.RectTransform))
            {
                CanBeFocused = false,
                ScrollBarVisible = true
            };

            foreach (ContentPackage contentPackage in ContentPackage.List)
            {
                var tickBox = new GUITickBox(new RectTransform(tickBoxScale, contentPackageList.Content.RectTransform, scaleBasis: ScaleBasis.BothHeight), contentPackage.Name)
                {
                    UserData = contentPackage,
                    OnSelected = SelectContentPackage,
                    Selected = SelectedContentPackages.Contains(contentPackage)
                };
                if (!contentPackage.IsCompatible())
                {
                    tickBox.TextColor = Color.Red;
                    tickBox.Enabled = false;
                    tickBox.ToolTip = TextManager.GetWithVariables(contentPackage.GameVersion <= new Version(0, 0, 0, 0) ? "IncompatibleContentPackageUnknownVersion" : "IncompatibleContentPackage",
                        new string[3] { "[packagename]", "[packageversion]", "[gameversion]" }, new string[3] { contentPackage.Name, contentPackage.GameVersion.ToString(), GameMain.Version.ToString() });
                }
                else if (contentPackage.CorePackage && !contentPackage.ContainsRequiredCorePackageFiles(out List<ContentType> missingContentTypes))
                {
                    tickBox.TextColor = Color.Red;
                    tickBox.Enabled = false;
                    tickBox.ToolTip = TextManager.GetWithVariables("ContentPackageMissingCoreFiles", new string[2] { "[packagename]", "[missingfiletypes]" },
                        new string[2] { contentPackage.Name, string.Join(", ", missingContentTypes) }, new bool[2] { false, true });
                }
            }

            new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.045f), generalLayoutGroup.RectTransform), TextManager.Get("Language"));
            var languageDD = new GUIDropDown(new RectTransform(new Vector2(1.0f, 0.045f), generalLayoutGroup.RectTransform));
            foreach (string language in TextManager.AvailableLanguages)
            {
                //TODO: display the name of the language in the target language?
                languageDD.AddItem(language, language);
            }
            languageDD.SelectItem(TextManager.Language);
            languageDD.OnSelected = (guiComponent, obj) =>
            {
                string newLanguage = obj as string;
                if (newLanguage == Language) return true;
                
                Language = newLanguage;
                UnsavedSettings = true;

                var msgBox = new GUIMessageBox(TextManager.Get("RestartRequiredLabel"), TextManager.Get("RestartRequiredLanguage"));
                //change fonts to the default font of the new language to make sure
                //they can be displayed when for example changing from English to Chinese
                var defaultFont = GUI.Style.LoadCurrentDefaultFont();
                msgBox.Header.Font = defaultFont;
                msgBox.Text.Font = defaultFont;

                return true;
            };

            var rightPanel = new GUILayoutGroup(new RectTransform(new Vector2(0.99f - leftPanel.RectTransform.RelativeSize.X, 0.95f),
                settingsFramePadding.RectTransform, Anchor.TopLeft));

            var tabButtonHolder = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 0.05f), rightPanel.RectTransform, Anchor.TopCenter), isHorizontal: true);

            var paddedFrame = new GUIFrame(new RectTransform(new Vector2(1.0f, 1.0f), rightPanel.RectTransform, Anchor.Center), style: null);


            tabs = new GUIFrame[Enum.GetValues(typeof(Tab)).Length];
            tabButtons = new GUIButton[tabs.Length];
            foreach (Tab tab in Enum.GetValues(typeof(Tab)))
            {
                tabs[(int)tab] = new GUIFrame(new RectTransform(new Vector2(1.0f, 0.91f), paddedFrame.RectTransform), style: "InnerFrame")
                {
                    UserData = tab
                };
                tabButtons[(int)tab] = new GUIButton(new RectTransform(new Vector2(0.25f, 1.0f), tabButtonHolder.RectTransform),
                    TextManager.Get("SettingsTab." + tab.ToString()), style: "GUITabButton")
                {
                    UserData = tab,
                    OnClicked = (bt, userdata) => { SelectTab((Tab)userdata); return true; }
                };
            }

            new GUIButton(new RectTransform(new Vector2(0.05f, 0.75f), tabButtonHolder.RectTransform, Anchor.BottomRight) { RelativeOffset = new Vector2(0.0f, 0.2f) }, style: "GUIBugButton")
            {
                ToolTip = TextManager.Get("bugreportbutton"),
                OnClicked = (btn, userdata) => { GameMain.Instance.ShowBugReporter(); return true; }
            };

            var buttonArea = new GUIFrame(new RectTransform(new Vector2(1.0f, 0.08f), paddedFrame.RectTransform, Anchor.BottomCenter), style: null);

            /// Graphics tab --------------------------------------------------------------

            var leftColumn = new GUILayoutGroup(new RectTransform(new Vector2(0.46f, 0.95f), tabs[(int)Tab.Graphics].RectTransform, Anchor.TopLeft)
            { RelativeOffset = new Vector2(0.025f, 0.02f) })
            { RelativeSpacing = 0.01f };
            var rightColumn = new GUILayoutGroup(new RectTransform(new Vector2(0.46f, 0.95f), tabs[(int)Tab.Graphics].RectTransform, Anchor.TopRight)
            { RelativeOffset = new Vector2(0.025f, 0.02f) })
            { RelativeSpacing = 0.01f };

            var supportedDisplayModes = new List<DisplayMode>();
            foreach (DisplayMode mode in GraphicsAdapter.DefaultAdapter.SupportedDisplayModes)
            {
                if (supportedDisplayModes.Any(m => m.Width == mode.Width && m.Height == mode.Height)) { continue; }
#if OSX
                // Monogame currently doesn't support retina displays
                // so we need to disable resolutions above the viewport size.

                // In a bundled .app you just disable HiDPI in the info.plist
                // but that's probably not gonna happen.
                if (mode.Width > GameMain.Instance.GraphicsDevice.DisplayMode.Width || mode.Height > GameMain.Instance.GraphicsDevice.DisplayMode.Height) { continue; }
#endif
                supportedDisplayModes.Add(mode);
            }

            new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.05f), leftColumn.RectTransform), TextManager.Get("Resolution"));
            var resolutionDD = new GUIDropDown(new RectTransform(new Vector2(1.0f, 0.05f), leftColumn.RectTransform), elementCount: supportedDisplayModes.Count)
            {
                OnSelected = SelectResolution,
#if !LINUX
                ButtonEnabled = GameMain.Config.WindowMode == WindowMode.Windowed
#endif
        };

            foreach (DisplayMode mode in supportedDisplayModes)
            {
                if (mode.Width < MinSupportedResolution.X || mode.Height < MinSupportedResolution.Y) { continue; }
                resolutionDD.AddItem(mode.Width + "x" + mode.Height, mode);
                if (GraphicsWidth == mode.Width && GraphicsHeight == mode.Height) resolutionDD.SelectItem(mode);
            }

            if (resolutionDD.SelectedItemData == null)
            {
                resolutionDD.SelectItem(GraphicsAdapter.DefaultAdapter.SupportedDisplayModes.Last());
            }

            new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.05f), leftColumn.RectTransform), TextManager.Get("DisplayMode"));
            var displayModeDD = new GUIDropDown(new RectTransform(new Vector2(1.0f, 0.05f), leftColumn.RectTransform));

            displayModeDD.AddItem(TextManager.Get("Fullscreen"), WindowMode.Fullscreen);
            displayModeDD.AddItem(TextManager.Get("Windowed"), WindowMode.Windowed);
#if (!OSX)
            displayModeDD.AddItem(TextManager.Get("BorderlessWindowed"), WindowMode.BorderlessWindowed);
            displayModeDD.SelectItem(GameMain.Config.WindowMode);
#else
            // Fullscreen option will just set itself to borderless on macOS.
            if (GameMain.Config.WindowMode == WindowMode.BorderlessWindowed)
            {
                displayModeDD.SelectItem(WindowMode.Fullscreen);
            }
            else
            {
                displayModeDD.SelectItem(GameMain.Config.WindowMode);
            }
#endif
            displayModeDD.OnSelected = (guiComponent, obj) =>
            {
                UnsavedSettings = true;
                GameMain.Config.WindowMode = (WindowMode)guiComponent.UserData;
#if !LINUX
                resolutionDD.ButtonEnabled = GameMain.Config.WindowMode == WindowMode.Windowed;
#endif
                return true;
            };

            GUITickBox vsyncTickBox = new GUITickBox(new RectTransform(tickBoxScale, leftColumn.RectTransform, scaleBasis: ScaleBasis.BothHeight), TextManager.Get("EnableVSync"))
            {
                ToolTip = TextManager.Get("EnableVSyncToolTip"),
                OnSelected = (GUITickBox box) =>
                {
                    VSyncEnabled = box.Selected;
                    GameMain.GraphicsDeviceManager.SynchronizeWithVerticalRetrace = VSyncEnabled;
                    GameMain.GraphicsDeviceManager.ApplyChanges();
                    UnsavedSettings = true;

                    return true;
                },
                Selected = VSyncEnabled
            };

            //TODO: remove hardcoded texts after the texts have been added to localization
            GUITickBox pauseOnFocusLostBox = new GUITickBox(new RectTransform(tickBoxScale, leftColumn.RectTransform, scaleBasis: ScaleBasis.BothHeight), 
                TextManager.Get("PauseOnFocusLost", returnNull: true) ?? "Pause on focus lost");
            pauseOnFocusLostBox.Selected = PauseOnFocusLost;
            pauseOnFocusLostBox.ToolTip = TextManager.Get("PauseOnFocusLostToolTip", returnNull: true) ?? "Pauses the game when its window is not in focus. Note that the game won't be paused when a multiplayer session is active.";
            pauseOnFocusLostBox.OnSelected = (tickBox) =>
            {
                PauseOnFocusLost = tickBox.Selected;
                UnsavedSettings = true;
                return true;
            };

            GUITextBlock particleLimitText = new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.05f), rightColumn.RectTransform), TextManager.Get("ParticleLimit"));
            GUIScrollBar particleScrollBar = new GUIScrollBar(new RectTransform(new Vector2(1.0f, 0.05f), rightColumn.RectTransform),
                barSize: 0.1f)
            {
                UserData = particleLimitText,
                BarScroll = (ParticleLimit - 200) / 1300.0f,
                OnMoved = (scrollBar, scroll) =>
                {
                    ChangeSliderText(scrollBar, scroll);
                    ParticleLimit = 200 + (int)(scroll * 1300.0f);
                    return true;
                },
                Step = 0.1f
            };
            particleScrollBar.OnMoved(particleScrollBar, particleScrollBar.BarScroll);

            new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.05f), rightColumn.RectTransform), TextManager.Get("LosEffect"));
            var losModeDD = new GUIDropDown(new RectTransform(new Vector2(1.0f, 0.05f), rightColumn.RectTransform));
            losModeDD.AddItem(TextManager.Get("LosModeNone"), LosMode.None);
            losModeDD.AddItem(TextManager.Get("LosModeTransparent"), LosMode.Transparent);
            losModeDD.AddItem(TextManager.Get("LosModeOpaque"), LosMode.Opaque);
            losModeDD.SelectItem(GameMain.Config.LosMode);
            losModeDD.OnSelected = (guiComponent, obj) =>
            {
                UnsavedSettings = true;
                GameMain.Config.LosMode = (LosMode)guiComponent.UserData;
                //don't allow changing los mode when playing as a client
                if (GameMain.Client == null)
                {
                    GameMain.LightManager.LosMode = GameMain.Config.LosMode;
                }
                return true;
            };

            GUITextBlock LightText = new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.05f), rightColumn.RectTransform), TextManager.Get("LightMapScale"))
            {
                ToolTip = TextManager.Get("LightMapScaleToolTip")
            };
            GUIScrollBar lightScrollBar = new GUIScrollBar(new RectTransform(new Vector2(1.0f, 0.05f), rightColumn.RectTransform),
                barSize: 0.1f)
            {
                UserData = LightText,
                ToolTip = TextManager.Get("LightMapScaleToolTip"),
                BarScroll = MathUtils.InverseLerp(0.2f, 1.0f, LightMapScale),
                OnMoved = (scrollBar, barScroll) =>
                {
                    ChangeSliderText(scrollBar, barScroll);
                    LightMapScale = MathHelper.Lerp(0.2f, 1.0f, barScroll);
                    UnsavedSettings = true; return true;
                },
                Step = 0.25f
            };
            lightScrollBar.OnMoved(lightScrollBar, lightScrollBar.BarScroll);

            new GUITickBox(new RectTransform(tickBoxScale, rightColumn.RectTransform, scaleBasis: ScaleBasis.BothHeight), TextManager.Get("SpecularLighting"))
            {
                ToolTip = TextManager.Get("SpecularLightingToolTip"),
                Selected = SpecularityEnabled,
                OnSelected = (tickBox) =>
                {
                    SpecularityEnabled = tickBox.Selected;
                    UnsavedSettings = true;
                    return true;
                }
            };

            new GUITickBox(new RectTransform(tickBoxScale, rightColumn.RectTransform, scaleBasis: ScaleBasis.BothHeight), TextManager.Get("ChromaticAberration"))
            {
                ToolTip = TextManager.Get("ChromaticAberrationToolTip"),
                Selected = ChromaticAberrationEnabled,
                OnSelected = (tickBox) =>
                {
                    ChromaticAberrationEnabled = tickBox.Selected;
                    UnsavedSettings = true;
                    return true;
                }
            };

            GUITextBlock HUDScaleText = new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.05f), rightColumn.RectTransform), TextManager.Get("HUDScale"));
            GUIScrollBar HUDScaleScrollBar = new GUIScrollBar(new RectTransform(new Vector2(1.0f, 0.05f), rightColumn.RectTransform),
                barSize: 0.1f)
            {
                UserData = HUDScaleText,
                BarScroll = (HUDScale - MinHUDScale) / (MaxHUDScale - MinHUDScale),
                OnMoved = (scrollBar, scroll) =>
                {
                    ChangeSliderText(scrollBar, scroll);
                    HUDScale = MathHelper.Lerp(MinHUDScale, MaxHUDScale, scroll);
                    UnsavedSettings = true;
                    OnHUDScaleChanged?.Invoke();
                    return true;
                },
                Step = 0.05f
            };
            HUDScaleScrollBar.OnMoved(HUDScaleScrollBar, HUDScaleScrollBar.BarScroll);

            GUITextBlock inventoryScaleText = new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.05f), rightColumn.RectTransform), TextManager.Get("InventoryScale"));
            GUIScrollBar inventoryScaleScrollBar = new GUIScrollBar(new RectTransform(new Vector2(1.0f, 0.05f), rightColumn.RectTransform), barSize: 0.1f)
            {
                UserData = inventoryScaleText,
                BarScroll = (InventoryScale - MinInventoryScale) / (MaxInventoryScale - MinInventoryScale),
                OnMoved = (scrollBar, scroll) =>
                {
                    ChangeSliderText(scrollBar, scroll);
                    InventoryScale = MathHelper.Lerp(MinInventoryScale, MaxInventoryScale, scroll);
                    UnsavedSettings = true;
                    return true;
                },
                Step = 0.05f
            };
            inventoryScaleScrollBar.OnMoved(inventoryScaleScrollBar, inventoryScaleScrollBar.BarScroll);

            /// Audio tab ----------------------------------------------------------------

            var audioSliders = new GUILayoutGroup(new RectTransform(new Vector2(0.95f, 0.95f), tabs[(int)Tab.Audio].RectTransform, Anchor.TopCenter)
                { RelativeOffset = new Vector2(0.0f, 0.02f) })
                { RelativeSpacing = 0.01f };

            GUITextBlock soundVolumeText = new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.05f), audioSliders.RectTransform), TextManager.Get("SoundVolume"));
            GUIScrollBar soundScrollBar = new GUIScrollBar(new RectTransform(new Vector2(1.0f, 0.05f), audioSliders.RectTransform),
                barSize: 0.05f)
            {
                UserData = soundVolumeText,
                BarScroll = SoundVolume,
                OnMoved = (scrollBar, scroll) =>
                {
                    ChangeSliderText(scrollBar, scroll);
                    SoundVolume = scroll;
                    return true;
                },
                Step = 0.05f
            };
            soundScrollBar.OnMoved(soundScrollBar, soundScrollBar.BarScroll);

            GUITextBlock musicVolumeText = new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.05f), audioSliders.RectTransform), TextManager.Get("MusicVolume"));
            GUIScrollBar musicScrollBar = new GUIScrollBar(new RectTransform(new Vector2(1.0f, 0.05f), audioSliders.RectTransform),
                barSize: 0.05f)
            {
                UserData = musicVolumeText,
                BarScroll = MusicVolume,
                OnMoved = (scrollBar, scroll) =>
                {
                    ChangeSliderText(scrollBar, scroll);
                    MusicVolume = scroll;
                    return true;
                },
                Step = 0.05f
            };
            musicScrollBar.OnMoved(musicScrollBar, musicScrollBar.BarScroll);

            GUITextBlock voiceChatVolumeText = new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.05f), audioSliders.RectTransform), TextManager.Get("VoiceChatVolume"));
            GUIScrollBar voiceChatScrollBar = new GUIScrollBar(new RectTransform(new Vector2(1.0f, 0.05f), audioSliders.RectTransform),
                barSize: 0.05f)
            {
                UserData = voiceChatVolumeText,
                BarScroll = VoiceChatVolume,
                OnMoved = (scrollBar, scroll) =>
                {
                    ChangeSliderText(scrollBar, scroll);
                    VoiceChatVolume = scroll;
                    return true;
                },
                Step = 0.05f
            };
            voiceChatScrollBar.OnMoved(voiceChatScrollBar, voiceChatScrollBar.BarScroll);

            GUITickBox muteOnFocusLostBox = new GUITickBox(new RectTransform(tickBoxScale, audioSliders.RectTransform, scaleBasis: ScaleBasis.BothHeight), TextManager.Get("MuteOnFocusLost"));
            muteOnFocusLostBox.Selected = MuteOnFocusLost;
            muteOnFocusLostBox.ToolTip = TextManager.Get("MuteOnFocusLostToolTip");
            muteOnFocusLostBox.OnSelected = (tickBox) =>
            {
                MuteOnFocusLost = tickBox.Selected;
                UnsavedSettings = true;
                return true;
            };

            new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.05f), audioSliders.RectTransform), TextManager.Get("VoiceChat"));

            IList<string> deviceNames = Alc.GetStringList((IntPtr)null, Alc.CaptureDeviceSpecifier);
            foreach (string name in deviceNames)
            {
                DebugConsole.NewMessage(name + " " + name.Length.ToString(), Color.Lime);
            }

            GUITickBox directionalVoiceChat = new GUITickBox(new RectTransform(tickBoxScale, audioSliders.RectTransform, scaleBasis: ScaleBasis.BothHeight), TextManager.Get("DirectionalVoiceChat"));
            directionalVoiceChat.Selected = UseDirectionalVoiceChat;
            directionalVoiceChat.ToolTip = TextManager.Get("DirectionalVoiceChatToolTip");
            directionalVoiceChat.OnSelected = (tickBox) =>
            {
                UseDirectionalVoiceChat = tickBox.Selected;
                UnsavedSettings = true;
                return true;
            };

            if (string.IsNullOrWhiteSpace(VoiceCaptureDevice) || !(deviceNames?.Contains(VoiceCaptureDevice) ?? false))
            {
                VoiceCaptureDevice = deviceNames?.Count > 0 ? deviceNames[0] : null;
            }
            if (string.IsNullOrWhiteSpace(VoiceCaptureDevice))
            {
                VoiceSetting = VoiceMode.Disabled;
            }
#if (!OSX)
            var deviceList = new GUIDropDown(new RectTransform(new Vector2(1.0f, 0.05f), audioSliders.RectTransform), TrimAudioDeviceName(VoiceCaptureDevice), deviceNames.Count);
            if (deviceNames?.Count > 0)
            {
                foreach (string name in deviceNames)
                {
                    deviceList.AddItem(TrimAudioDeviceName(name), name);
                }
                deviceList.OnSelected = (GUIComponent selected, object obj) =>
                {
                    string name = obj as string;
                    if (VoiceCaptureDevice == name) { return true; }

                    VoipCapture.ChangeCaptureDevice(name);
                    return true;
                };
            }
            else
            {
                deviceList.AddItem(TextManager.Get("VoipNoDevices") ?? "N/A", null);
                deviceList.ButtonEnabled = false;
                deviceList.Select(0);
            }

#else
            var defaultDeviceGroup = new GUILayoutGroup(new RectTransform(new Vector2(1f, 0.1f), audioSliders.RectTransform), true, Anchor.CenterLeft);
            var currentDeviceTextBlock = new GUITextBlock(new RectTransform(new Vector2(.7f, 0.75f), null), 
                TextManager.AddPunctuation(':', TextManager.Get("CurrentDevice"), TrimAudioDeviceName(VoiceCaptureDevice)))
            {
                ToolTip = TextManager.Get("CurrentDeviceToolTip.OSX"),
                TextAlignment = Alignment.CenterLeft
            };

            string refreshText = ToolBox.WrapText(TextManager.Get("RefreshDefaultDevice"), defaultDeviceGroup.RectTransform.Rect.Width * 0.3f, GUI.Font);
            var currentDeviceButton = new GUIButton(new RectTransform(new Vector2(.3f, 0.75f), defaultDeviceGroup.RectTransform), refreshText)
            {
                ToolTip = TextManager.Get("RefreshDefaultDeviceToolTip"),
                OnClicked = (bt, userdata) =>
                {
                    deviceNames = Alc.GetStringList((IntPtr)null, Alc.CaptureDeviceSpecifier);
                    if (deviceNames?.Count > 0)
                    {
                        if (VoiceCaptureDevice == deviceNames[0]) return true;

                        VoipCapture.ChangeCaptureDevice(deviceNames[0]);
                        currentDeviceTextBlock.Text = TextManager.AddPunctuation(':', TextManager.Get("CurrentDevice"), TrimAudioDeviceName(VoiceCaptureDevice));
                        currentDeviceTextBlock.Flash(Color.Blue);
                    }
                    else
                    {
                        currentDeviceTextBlock.Text = TextManager.Get("VoipNoDevices") ?? "N/A";
                        currentDeviceTextBlock.Flash(Color.Red);
                    }

                    return true;
                }
            };
            currentDeviceButton.OnClicked(currentDeviceButton, null);

            currentDeviceTextBlock.RectTransform.Parent = defaultDeviceGroup.RectTransform;
#endif
            //var radioButtonFrame = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 0.12f), audioSliders.RectTransform));

            GUIRadioButtonGroup voiceMode = new GUIRadioButtonGroup();
            for (int i = 0; i < 3; i++)
            {
                string langStr = "VoiceMode." + ((VoiceMode)i).ToString();
                var tick = new GUITickBox(new RectTransform(tickBoxScale, audioSliders.RectTransform, scaleBasis: ScaleBasis.BothHeight), TextManager.Get(langStr))
                {
                    ToolTip = TextManager.Get(langStr + "ToolTip")
                };

                voiceMode.AddRadioButton((VoiceMode)i, tick);
            }

            var micVolumeText = new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.05f), audioSliders.RectTransform), TextManager.Get("MicrophoneVolume"));
            var micVolumeSlider = new GUIScrollBar(new RectTransform(new Vector2(1.0f, 0.05f), audioSliders.RectTransform),
                barSize: 0.05f)
            {
                UserData = micVolumeText,
                BarScroll = (float)Math.Sqrt(MathUtils.InverseLerp(0.2f, 5.0f, MicrophoneVolume)),
                OnMoved = (scrollBar, scroll) =>
                {
                    MicrophoneVolume = MathHelper.Lerp(0.2f, 5.0f, scroll * scroll);
                    MicrophoneVolume = (float)Math.Round(MicrophoneVolume, 1);
                    ChangeSliderText(scrollBar, MicrophoneVolume);
                    scrollBar.Step = 0.05f;
                    return true;
                },
                Step = 0.05f
            };
            micVolumeSlider.OnMoved(micVolumeSlider, micVolumeSlider.BarScroll);


            var extraVoiceSettingsContainer = new GUIFrame(new RectTransform(new Vector2(1.0f, 0.2f), audioSliders.RectTransform, Anchor.BottomCenter), style: null);

            var voiceInputContainer = new GUILayoutGroup(new RectTransform(Vector2.One, extraVoiceSettingsContainer.RectTransform, Anchor.BottomCenter));
            new GUITextBlock(new RectTransform(new Vector2(0.6f, 0.25f), voiceInputContainer.RectTransform), TextManager.Get("InputType.Voice"));
            var voiceKeyBox = new GUITextBox(new RectTransform(new Vector2(0.4f, 0.25f), voiceInputContainer.RectTransform, Anchor.TopRight),
                text: keyMapping[(int)InputType.Voice].ToString())
            {
                UserData = InputType.Voice
            };
            voiceKeyBox.OnSelected += KeyBoxSelected;
            voiceKeyBox.SelectedColor = Color.Gold * 0.3f;

            var voiceActivityGroup = new GUILayoutGroup(new RectTransform(Vector2.One, extraVoiceSettingsContainer.RectTransform));
            GUITextBlock noiseGateText = new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.25f), voiceActivityGroup.RectTransform), TextManager.Get("NoiseGateThreshold"))
            {
                TextGetter = () =>
                {
                    return TextManager.Get("NoiseGateThreshold") + " " + ((int)NoiseGateThreshold).ToString() + " dB";
                }
            };
            var dbMeter = new GUIProgressBar(new RectTransform(new Vector2(1.0f, 0.25f), voiceActivityGroup.RectTransform), 0.0f, Color.Lime);
            dbMeter.ProgressGetter = () =>
            {
                if (VoipCapture.Instance == null) return 0.0f;
                dbMeter.Color = VoipCapture.Instance.LastdB > NoiseGateThreshold ? Color.Lime : Color.Orange; //TODO: i'm a filthy hack
                return ((float)VoipCapture.Instance.LastdB + 100.0f) / 100.0f;
            };
            var noiseGateSlider = new GUIScrollBar(new RectTransform(Vector2.One, dbMeter.RectTransform, Anchor.Center), color: Color.White, barSize: 0.03f);
            noiseGateSlider.Frame.Visible = false;
            noiseGateSlider.Step = 0.01f;
            noiseGateSlider.Range = new Vector2(-100.0f, 0.0f);
            noiseGateSlider.BarScrollValue = NoiseGateThreshold;
            noiseGateSlider.OnMoved = (GUIScrollBar scrollBar, float barScroll) =>
            {
                NoiseGateThreshold = scrollBar.BarScrollValue;
                UnsavedSettings = true;
                return true;
            };

            voiceMode.OnSelect = (GUIRadioButtonGroup rbg, Enum value) =>
            {
                if (rbg.Selected != null && rbg.Selected.Equals(value)) return;
                try
                {
                    VoiceMode vMode = (VoiceMode)value;
                    VoiceSetting = vMode;
                    if (vMode == VoiceMode.Activity)
                    {
                        voiceActivityGroup.Visible = true;
                        if (GameMain.Client == null && VoipCapture.Instance == null)
                        {
                            VoipCapture.Create(GameMain.Config.VoiceCaptureDevice);
                            if (VoipCapture.Instance == null)
                            {
                                VoiceSetting = vMode = VoiceMode.Disabled;
                                voiceInputContainer.Visible = false;
                                voiceActivityGroup.Visible = false;
                                return;
                            }
                        }
                    }
                    else
                    {
                        voiceActivityGroup.Visible = false;
                        if (GameMain.Client == null)
                        {
                            VoipCapture.Instance?.Dispose();
                        }
                    }

                    voiceInputContainer.Visible = (vMode == VoiceMode.PushToTalk);
                    UnsavedSettings = true;
                }
                catch (Exception e)
                {
                    DebugConsole.ThrowError("Failed to set voice capture mode.", e);
                    GameAnalyticsManager.AddErrorEventOnce("SetVoiceCaptureMode", GameAnalyticsSDK.Net.EGAErrorSeverity.Error, "Failed to set voice capture mode. " + e.Message + "\n" + e.StackTrace);
                    VoiceSetting = VoiceMode.Disabled;
                }
            };
            voiceMode.Selected = VoiceSetting;
            if (string.IsNullOrWhiteSpace(VoiceCaptureDevice))
            {
                voiceMode.Enabled = false;
            }

            /// Controls tab -------------------------------------------------------------
            var controlsLayoutGroup = new GUILayoutGroup(new RectTransform(new Vector2(0.95f, 0.95f), tabs[(int)Tab.Controls].RectTransform, Anchor.TopCenter)
            { RelativeOffset = new Vector2(0.0f, 0.02f) })
            { RelativeSpacing = 0.01f };

            GUITextBlock aimAssistText = new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.05f), controlsLayoutGroup.RectTransform), TextManager.Get("AimAssist"))
            {
                ToolTip = TextManager.Get("AimAssistToolTip")
            };
            GUIScrollBar aimAssistSlider = new GUIScrollBar(new RectTransform(new Vector2(1.0f, 0.05f), controlsLayoutGroup.RectTransform),
                barSize: 0.05f)
            {
                UserData = aimAssistText,
                BarScroll = MathUtils.InverseLerp(0.0f, 5.0f, AimAssistAmount),
                ToolTip = TextManager.Get("AimAssistToolTip"),
                OnMoved = (scrollBar, scroll) =>
                {
                    ChangeSliderText(scrollBar, scroll);
                    AimAssistAmount = MathHelper.Lerp(0.0f, 5.0f, scroll);
                    return true;
                },
                Step = 0.1f
            };
            aimAssistSlider.OnMoved(aimAssistSlider, aimAssistSlider.BarScroll);

            new GUITickBox(new RectTransform(tickBoxScale, controlsLayoutGroup.RectTransform, scaleBasis: ScaleBasis.BothHeight), TextManager.Get("EnableMouseLook"))
            {
                ToolTip = TextManager.Get("EnableMouseLookToolTip"),
                Selected = EnableMouseLook,
                OnSelected = (tickBox) =>
                {
                    EnableMouseLook = tickBox.Selected;
                    UnsavedSettings = true;
                    return true;
                }
            };

            var inputFrame = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 0.75f), controlsLayoutGroup.RectTransform), isHorizontal: true)
                { Stretch = true, RelativeSpacing = 0.03f };

            var inputColumnLeft = new GUILayoutGroup(new RectTransform(new Vector2(0.5f, 1.0f), inputFrame.RectTransform))
                { Stretch = true, RelativeSpacing = 0.02f };
            var inputColumnRight = new GUILayoutGroup(new RectTransform(new Vector2(0.5f, 1.0f), inputFrame.RectTransform))
                { Stretch = true, RelativeSpacing = 0.02f };

            var inputNames = Enum.GetValues(typeof(InputType));
            var inputNameBlocks = new List<GUITextBlock>();
            for (int i = 0; i < inputNames.Length; i++)
            {
                var inputContainer = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 0.06f),(i <= (inputNames.Length / 2.2f) ? inputColumnLeft : inputColumnRight).RectTransform))
                    { Stretch = true, IsHorizontal = true, RelativeSpacing = 0.05f, Color = new Color(12, 14, 15, 215) };
                var inputName = new GUITextBlock(new RectTransform(new Vector2(0.7f, 1.0f), inputContainer.RectTransform, Anchor.TopLeft) { MinSize = new Point(150, 0) },
                    TextManager.Get("InputType." + ((InputType)i)), font: GUI.SmallFont) { ForceUpperCase = true };
                inputNameBlocks.Add(inputName);
                var keyBox = new GUITextBox(new RectTransform(new Vector2(0.3f, 1.0f), inputContainer.RectTransform),
                    text: keyMapping[i].ToString(), font: GUI.SmallFont)
                {
                    UserData = i
                };
                keyBox.OnSelected += KeyBoxSelected;
                keyBox.SelectedColor = Color.Gold * 0.3f;
            }

            GUITextBlock.AutoScaleAndNormalize(inputNameBlocks);

            var resetControlsHolder = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 0.07f), controlsLayoutGroup.RectTransform), isHorizontal: true)
            {
                RelativeSpacing = 0.02f
            };

            var defaultBindingsButton = new GUIButton(new RectTransform(new Vector2(0.3f, 1.0f), resetControlsHolder.RectTransform), TextManager.Get("SetDefaultBindings"))
            {
                ToolTip = TextManager.Get("SetDefaultBindingsToolTip"),
                OnClicked = (button, data) =>
                {
                    ResetControls(legacy: false);
                    return true;
                }
            };

            var legacyBindingsButton = new GUIButton(new RectTransform(new Vector2(0.3f, 1.0f), resetControlsHolder.RectTransform), TextManager.Get("SetLegacyBindings"))
            {
                ToolTip = TextManager.Get("SetLegacyBindingsToolTip"),
                OnClicked = (button, data) =>
                {
                    ResetControls(legacy: true);
                    return true;
                }
            };

            legacyBindingsButton.TextBlock.RectTransform.SizeChanged += () =>
            {
                GUITextBlock.AutoScaleAndNormalize(defaultBindingsButton.TextBlock, legacyBindingsButton.TextBlock);
            };

            //spacing
            new GUIFrame(new RectTransform(new Vector2(1.0f, 0.02f), generalLayoutGroup.RectTransform), style: null);

            new GUIButton(new RectTransform(new Vector2(0.3f, 1.0f), buttonArea.RectTransform, Anchor.BottomLeft),
                TextManager.Get("Cancel"), style: "GUIButtonLarge")
            {
                IgnoreLayoutGroups = true,
                OnClicked = (x, y) =>
                {
                    if (UnsavedSettings)
                    {
                        LoadPlayerConfig();
                    }
                    if (Screen.Selected == GameMain.MainMenuScreen) GameMain.MainMenuScreen.ReturnToMainMenu(null, null);
                    GUI.SettingsMenuOpen = false;
                    return true;
                }
            };

            new GUIButton(new RectTransform(new Vector2(0.3f, 1.0f), buttonArea.RectTransform, Anchor.BottomCenter),
                TextManager.Get("Reset"), style: "GUIButtonLarge")
            {
                IgnoreLayoutGroups = true,
                OnClicked = (button, data) =>
                {
                    // TODO: add a prompt
                    LoadDefaultConfig(setLanguage: false);
                    CheckBindings(true);
                    RefreshItemMessages();
                    ApplySettings();
                    if (Screen.Selected == GameMain.MainMenuScreen)
                    {
                        GameMain.MainMenuScreen.ResetSettingsFrame(currentTab);
                    }
                    else
                    {
                        ResetSettingsFrame();
                        CreateSettingsFrame(currentTab);
                    }
                    return true;
                }
            };

            applyButton = new GUIButton(new RectTransform(new Vector2(0.3f, 1.0f), buttonArea.RectTransform, Anchor.BottomRight),
                TextManager.Get("ApplySettingsButton"), style: "GUIButtonLarge")
            {
                IgnoreLayoutGroups = true,
                Enabled = false
            };
            applyButton.OnClicked = ApplyClicked;

            UnsavedSettings = false; // Reset unsaved settings to false once the UI has been created
            SelectTab(selectedTab);
        }

        private string TrimAudioDeviceName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) { return string.Empty; }
            string[] prefixes = { "OpenAL Soft on " };
            foreach (string prefix in prefixes)
            {
                if (name.StartsWith(prefix, StringComparison.InvariantCulture))
                {
                    return name.Remove(0, prefix.Length);
                }
            }
            return name;
        }
        
        private Tab currentTab;
        private void SelectTab(Tab tab)
        {
            switch (tab)
            {
                case Tab.Audio:
                    if (VoiceSetting == VoiceMode.Activity)
                    {
                        if (GameMain.Client == null && VoipCapture.Instance == null)
                        {
                            VoipCapture.Create(GameMain.Config.VoiceCaptureDevice);
                        }
                    }
                    break;
                default:
                    if (GameMain.Client == null)
                    {
                        VoipCapture.Instance?.Dispose();
                    }
                    break;
            }
            for (int i = 0; i < tabs.Length; i++)
            {
                tabs[i].Visible = (Tab)tabs[i].UserData == tab;
                tabButtons[i].Selected = tabs[i].Visible;
            }
            currentTab = tab;
        }

        private void KeyBoxSelected(GUITextBox textBox, Keys key)
        {
            textBox.Text = "";
            CoroutineManager.StartCoroutine(WaitForKeyPress(textBox));
        }

        private void ResetControls(bool legacy)
        {
            // TODO: add a prompt?
            SetDefaultBindings(legacy: legacy);
            CheckBindings(true);
            RefreshItemMessages();
            ApplySettings();
            if (Screen.Selected == GameMain.MainMenuScreen)
            {
                GameMain.MainMenuScreen.ResetSettingsFrame(Tab.Controls);
            }
            else
            {
                ResetSettingsFrame();
                CreateSettingsFrame(Tab.Controls);
            }
        }

        private bool SelectResolution(GUIComponent selected, object userData)
        {
            DisplayMode mode = selected.UserData as DisplayMode;
            if (mode == null) return false;

            if (GraphicsWidth == mode.Width && GraphicsHeight == mode.Height) return false;

            GraphicsWidth = mode.Width;
            GraphicsHeight = mode.Height;
            GameMain.Instance.ApplyGraphicsSettings();
            UnsavedSettings = true;

            return true;
        }

        private bool SelectContentPackage(GUITickBox tickBox)
        {
            var contentPackage = tickBox.UserData as ContentPackage;
            if (contentPackage.CorePackage)
            {
                if (tickBox.Selected)
                {
                    //make sure no other core packages are selected
                    SelectedContentPackages.RemoveWhere(cp => cp.CorePackage && cp != contentPackage);
                    SelectedContentPackages.Add(contentPackage);
                    foreach (GUITickBox otherTickBox in tickBox.Parent.Children)
                    {
                        ContentPackage otherContentPackage = otherTickBox.UserData as ContentPackage;
                        if (otherContentPackage == contentPackage) continue;
                        otherTickBox.Selected = SelectedContentPackages.Contains(otherContentPackage);
                    }
                }
                else if (SelectedContentPackages.Contains(contentPackage))
                {
                    //core packages cannot be deselected, only switched by selecting another core package
                    new GUIMessageBox(TextManager.Get("Warning"), TextManager.Get("CorePackageRequiredWarning"));
                    tickBox.Selected = true;
                    return true;
                }
            }
            else
            {
                if (tickBox.Selected)
                {
                    SelectedContentPackages.Add(contentPackage);
                }
                else
                {
                    SelectedContentPackages.Remove(contentPackage);
                }
            }
            UnsavedSettings = true;
            return true;
        }

        private IEnumerable<object> WaitForKeyPress(GUITextBox keyBox)
        {
            yield return CoroutineStatus.Running;

            while (PlayerInput.LeftButtonHeld() || PlayerInput.LeftButtonClicked())
            {
                //wait for the mouse to be released, so that we don't interpret clicking on the textbox as the keybinding
                yield return CoroutineStatus.Running;
            }
            while (keyBox.Selected && PlayerInput.GetKeyboardState.GetPressedKeys().Length == 0 &&
                !PlayerInput.LeftButtonClicked() && !PlayerInput.RightButtonClicked() && !PlayerInput.MidButtonClicked() &&
                !PlayerInput.Mouse4ButtonClicked() && !PlayerInput.Mouse5ButtonClicked() && !PlayerInput.MouseWheelUpClicked() && !PlayerInput.MouseWheelDownClicked())
            {
                if (Screen.Selected != GameMain.MainMenuScreen && !GUI.SettingsMenuOpen) yield return CoroutineStatus.Success;

                yield return CoroutineStatus.Running;
            }

            UnsavedSettings = true;

            int keyIndex = (int)keyBox.UserData;

            if (PlayerInput.LeftButtonClicked())
            {
                keyMapping[keyIndex] = new KeyOrMouse(0);
                keyBox.Text = "Mouse1";
            }
            else if (PlayerInput.RightButtonClicked())
            {
                keyMapping[keyIndex] = new KeyOrMouse(1);
                keyBox.Text = "Mouse2";
            }
            else if (PlayerInput.MidButtonClicked())
            {
                keyMapping[keyIndex] = new KeyOrMouse(2);
                keyBox.Text = "Mouse3";
            }
            else if (PlayerInput.Mouse4ButtonClicked())
            {
                keyMapping[keyIndex] = new KeyOrMouse(3);
                keyBox.Text = "Mouse4";
            }
            else if (PlayerInput.Mouse5ButtonClicked())
            {
                keyMapping[keyIndex] = new KeyOrMouse(4);
                keyBox.Text = "Mouse5";
            }
            else if (PlayerInput.MouseWheelUpClicked())
            {
                keyMapping[keyIndex] = new KeyOrMouse(5);
                keyBox.Text = "MouseWheelUp";
            }
            else if (PlayerInput.MouseWheelDownClicked())
            {
                keyMapping[keyIndex] = new KeyOrMouse(6);
                keyBox.Text = "MouseWheelDown";
            }
            else if (PlayerInput.GetKeyboardState.GetPressedKeys().Length > 0)
            {
                Keys key = PlayerInput.GetKeyboardState.GetPressedKeys()[0];
                keyMapping[keyIndex] = new KeyOrMouse(key);
                keyBox.Text = key.ToString("G");
            }
            else
            {
                yield return CoroutineStatus.Success;
            }

            keyBox.Deselect();
            RefreshItemMessages();

            yield return CoroutineStatus.Success;
        }

        private void RefreshItemMessages()
        {
            foreach (Item item in Item.ItemList)
            {
                foreach (Items.Components.ItemComponent ic in item.Components)
                {
                    ic.ParseMsg();
                }
            }
        }

        private void ApplySettings()
        {
            SaveNewPlayerConfig();

            SettingsFrame.Flash(Color.Green);

            if (GameMain.WindowMode != GameMain.Config.WindowMode)
            {
                GameMain.Instance.ApplyGraphicsSettings();
            }

            if (GameMain.GraphicsWidth != GameMain.Config.GraphicsWidth || GameMain.GraphicsHeight != GameMain.Config.GraphicsHeight)
            {
#if OSX
                if (GameMain.Config.WindowMode != WindowMode.BorderlessWindowed)
                {
#endif
                new GUIMessageBox(TextManager.Get("RestartRequiredLabel"), TextManager.Get("RestartRequiredResolution"));
#if OSX
                }
#endif
            }
        }

        private bool ApplyClicked(GUIButton button, object userData)
        {
            ApplySettings();
            if (Screen.Selected != GameMain.MainMenuScreen) GUI.SettingsMenuOpen = false;

            return true;
        }
    }
}
