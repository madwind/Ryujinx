using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Ryujinx.Ava.UI.Helpers;
using Ryujinx.Ava.UI.Models.Input;
using Ryujinx.Ava.UI.ViewModels.Input;
using Ryujinx.Common.Configuration.Hid.Controller;
using Ryujinx.HLE.HOS.Services.Hid;
using Ryujinx.Input;
using Ryujinx.Input.Assigner;
using Ryujinx.Input.HLE;
using System;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;
using StickInputId = Ryujinx.Common.Configuration.Hid.Controller.StickInputId;

namespace Ryujinx.Ava.UI.Views.Input
{
    public partial class ControllerInputView : UserControl
    {
        private ButtonKeyAssigner _currentAssigner;
        private volatile bool _isRunning = true;
        private const float StickMaxPosition = 3;

        public ControllerInputView()
        {
            InitializeComponent();

            foreach (ILogical visual in SettingButtons.GetLogicalDescendants())
            {
                switch (visual)
                {
                    case ToggleButton button and not CheckBox:
                        button.IsCheckedChanged += Button_IsCheckedChanged;
                        break;
                    case CheckBox check:
                        check.IsCheckedChanged += CheckBox_IsCheckedChanged;
                        break;
                    case Slider slider:
                        slider.PropertyChanged += Slider_ValueChanged;
                        break;
                }
            }

            StartUpdatingData();
        }

        protected override void OnPointerReleased(PointerReleasedEventArgs e)
        {
            base.OnPointerReleased(e);

            if (_currentAssigner is { ToggledButton.IsPointerOver: false })
            {
                _currentAssigner.Cancel();
            }
        }

        private float _changeSlider = float.NaN;

        private void Slider_ValueChanged(object sender, AvaloniaPropertyChangedEventArgs e)
        {
            if (sender is Slider check)
            {
                _changeSlider = check.IsPointerOver switch
                {
                    true when float.IsNaN(_changeSlider) => (float)check.Value,
                    false => float.NaN,
                    _ => _changeSlider
                };

                if (!float.IsNaN(_changeSlider) && _changeSlider != (float)check.Value)
                {
                    (DataContext as ControllerInputViewModel)!.ParentModel.IsModified = true;
                    _changeSlider = (float)check.Value;
                }
            }
        }

        private void CheckBox_IsCheckedChanged(object sender, RoutedEventArgs e)
        {
            if (sender is CheckBox { IsPointerOver: true })
            {
                (DataContext as ControllerInputViewModel)!.ParentModel.IsModified = true;
                _currentAssigner?.Cancel();
                _currentAssigner = null;
            }
        }


        private void Button_IsCheckedChanged(object sender, RoutedEventArgs e)
        {
            if (sender is ToggleButton button)
            {
                if (button.IsChecked is true)
                {
                    if (_currentAssigner != null && button == _currentAssigner.ToggledButton)
                    {
                        return;
                    }

                    bool isStick = button.Tag != null && button.Tag.ToString() == "stick";

                    if (_currentAssigner == null)
                    {
                        _currentAssigner = new ButtonKeyAssigner(button);

                        this.Focus(NavigationMethod.Pointer);

                        PointerPressed += MouseClick;

                        var viewModel = (DataContext as ControllerInputViewModel);

                        IKeyboard keyboard =
                            (IKeyboard)viewModel.ParentModel.AvaloniaKeyboardDriver
                                .GetGamepad("0"); // Open Avalonia keyboard for cancel operations.
                        IButtonAssigner assigner = CreateButtonAssigner(isStick);

                        _currentAssigner.ButtonAssigned += (sender, e) =>
                        {
                            if (e.ButtonValue.HasValue)
                            {
                                var buttonValue = e.ButtonValue.Value;
                                viewModel.ParentModel.IsModified = true;

                                switch (button.Name)
                                {
                                    case "ButtonZl":
                                        viewModel.Config.ButtonZl = buttonValue.AsHidType<GamepadInputId>();
                                        break;
                                    case "ButtonL":
                                        viewModel.Config.ButtonL = buttonValue.AsHidType<GamepadInputId>();
                                        break;
                                    case "ButtonMinus":
                                        viewModel.Config.ButtonMinus = buttonValue.AsHidType<GamepadInputId>();
                                        break;
                                    case "LeftStickButton":
                                        viewModel.Config.LeftStickButton = buttonValue.AsHidType<GamepadInputId>();
                                        break;
                                    case "LeftJoystick":
                                        viewModel.Config.LeftJoystick = buttonValue.AsHidType<StickInputId>();
                                        break;
                                    case "DpadUp":
                                        viewModel.Config.DpadUp = buttonValue.AsHidType<GamepadInputId>();
                                        break;
                                    case "DpadDown":
                                        viewModel.Config.DpadDown = buttonValue.AsHidType<GamepadInputId>();
                                        break;
                                    case "DpadLeft":
                                        viewModel.Config.DpadLeft = buttonValue.AsHidType<GamepadInputId>();
                                        break;
                                    case "DpadRight":
                                        viewModel.Config.DpadRight = buttonValue.AsHidType<GamepadInputId>();
                                        break;
                                    case "LeftButtonSr":
                                        viewModel.Config.LeftButtonSr = buttonValue.AsHidType<GamepadInputId>();
                                        break;
                                    case "LeftButtonSl":
                                        viewModel.Config.LeftButtonSl = buttonValue.AsHidType<GamepadInputId>();
                                        break;
                                    case "RightButtonSr":
                                        viewModel.Config.RightButtonSr = buttonValue.AsHidType<GamepadInputId>();
                                        break;
                                    case "RightButtonSl":
                                        viewModel.Config.RightButtonSl = buttonValue.AsHidType<GamepadInputId>();
                                        break;
                                    case "ButtonZr":
                                        viewModel.Config.ButtonZr = buttonValue.AsHidType<GamepadInputId>();
                                        break;
                                    case "ButtonR":
                                        viewModel.Config.ButtonR = buttonValue.AsHidType<GamepadInputId>();
                                        break;
                                    case "ButtonPlus":
                                        viewModel.Config.ButtonPlus = buttonValue.AsHidType<GamepadInputId>();
                                        break;
                                    case "ButtonA":
                                        viewModel.Config.ButtonA = buttonValue.AsHidType<GamepadInputId>();
                                        break;
                                    case "ButtonB":
                                        viewModel.Config.ButtonB = buttonValue.AsHidType<GamepadInputId>();
                                        break;
                                    case "ButtonX":
                                        viewModel.Config.ButtonX = buttonValue.AsHidType<GamepadInputId>();
                                        break;
                                    case "ButtonY":
                                        viewModel.Config.ButtonY = buttonValue.AsHidType<GamepadInputId>();
                                        break;
                                    case "RightStickButton":
                                        viewModel.Config.RightStickButton = buttonValue.AsHidType<GamepadInputId>();
                                        break;
                                    case "RightJoystick":
                                        viewModel.Config.RightJoystick = buttonValue.AsHidType<StickInputId>();
                                        break;
                                }
                            }
                        };

                        _currentAssigner.GetInputAndAssign(assigner, keyboard);
                    }
                    else
                    {
                        if (_currentAssigner != null)
                        {
                            _currentAssigner.Cancel();
                            _currentAssigner = null;
                            button.IsChecked = false;
                        }
                    }
                }
                else
                {
                    _currentAssigner?.Cancel();
                    _currentAssigner = null;
                }
            }
        }

        private void MouseClick(object sender, PointerPressedEventArgs e)
        {
            bool shouldUnbind = e.GetCurrentPoint(this).Properties.IsMiddleButtonPressed;

            _currentAssigner?.Cancel(shouldUnbind);

            PointerPressed -= MouseClick;
        }

        private IButtonAssigner CreateButtonAssigner(bool forStick)
        {
            IButtonAssigner assigner;

            var controllerInputViewModel = DataContext as ControllerInputViewModel;

            assigner = new GamepadButtonAssigner(
                controllerInputViewModel.ParentModel.SelectedGamepad,
                (controllerInputViewModel.ParentModel.Config as StandardControllerInputConfig).TriggerThreshold,
                forStick);

            return assigner;
        }

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnDetachedFromVisualTree(e);
            _currentAssigner?.Cancel();
            _currentAssigner = null;
        }

        private void Control_OnUnloaded(object sender, RoutedEventArgs e)
        {
            _isRunning = false;
        }

        private string BuildSvgCss(IGamepad gamepad, GamepadInputConfig config, JoystickPosition leftPosition,
            JoystickPosition rightPosition)
        {
            gamepad.SetConfiguration(config.GetConfig());
            StringBuilder sb = new();
            for (int i = 0; i < (int)GamepadInputId.Count; i++)
            {
                GamepadButtonInputId button = (GamepadButtonInputId)i;
                if (gamepad.GetMappedStateSnapshot().IsPressed(button))
                {
                    sb.Append($"#{button}{{fill:#00bbdb;}}");
                }
            }

            sb.Append(
                $"#LeftStick{{transform: translate ({(float)leftPosition.Dx / short.MaxValue * StickMaxPosition} {-(float)leftPosition.Dy / short.MaxValue * StickMaxPosition});}}");
            sb.Append(
                $"#RightStick{{transform: translate ({(float)rightPosition.Dx / short.MaxValue * StickMaxPosition} {-(float)rightPosition.Dy / short.MaxValue * StickMaxPosition});}}");
            return sb.ToString();
        }

        private void StartUpdatingData()
        {
            Dispatcher.UIThread.InvokeAsync(async () =>
            {
                while (_isRunning)
                {
                    var viewModel = DataContext as ControllerInputViewModel;
                    if (viewModel != null)
                    {
                        IGamepad gamepad = viewModel.ParentModel.SelectedGamepad;
                        var config = viewModel.Config;
                        JoystickPosition leftPosition = default, rightposition = default;
                        if (config.LeftJoystick != StickInputId.Unbound)
                        {
                            var stickInputId = (Ryujinx.Input.StickInputId)(int)config.LeftJoystick;
                            (float leftAxisX, float leftAxisY) = gamepad.GetStick(stickInputId);
                            leftPosition = NpadController.GetJoystickPosition(leftAxisX, leftAxisY,
                                config.DeadzoneLeft, config.RangeLeft);
                            viewModel.LeftStickPosition = $"{leftPosition.Dx}, {leftPosition.Dy}";
                        }

                        if (config.RightJoystick != StickInputId.Unbound)
                        {
                            StickInputId stickInputId = config.RightJoystick;
                            (float rightAxisX, float rightAxisY) =
                                gamepad.GetStick((Ryujinx.Input.StickInputId)stickInputId);
                            rightposition = NpadController.GetJoystickPosition(rightAxisX, rightAxisY,
                                config.DeadzoneRight, config.RangeRight);
                            viewModel.RightStickPosition = $"{rightposition.Dx}, {rightposition.Dy}";
                        }

                        viewModel.UpdateImageCss(BuildSvgCss(gamepad, config, leftPosition, rightposition));

                        // 假设你已获得加速度计和陀螺仪数据
                        Vector3 accelerometerData = gamepad.GetMotionData(MotionInputId.Accelerometer);
                        Vector3 gyroscopeData = gamepad.GetMotionData(MotionInputId.Gyroscope);

                        LeftCubeCanvas.UpdateRotationFromMotionData(accelerometerData, gyroscopeData);
                        LeftCubeCanvas.InvalidateVisual();

                        Vector3 rightAccelerometer = gamepad.GetMotionData(MotionInputId.RightAccelerometer);
                        Vector3 rightGyroscope = gamepad.GetMotionData(MotionInputId.RightGyroscope);

                        RightCubeCanvas.UpdateRotationFromMotionData(rightAccelerometer, rightGyroscope, true);
                        RightCubeCanvas.InvalidateVisual();
                    }

                    await Task.Delay(16);
                }
            });
        }
    }
}
