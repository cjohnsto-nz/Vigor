using System;
using System.Linq;
using System.Reflection;
using Vintagestory.API.Client;

namespace Vigor.Hud
{
    internal sealed class HudUiVigorBridge : IDisposable
    {
        private const string BarId = "vigor:stamina";

        private readonly ICoreClientAPI _capi;
        private readonly HudVigorBar _vigorHud;
        private readonly Action<StaminaSnapshot> _snapshotHandler;

        private object _valueNotifier;
        private object _displayValueNotifier;
        private Type _controllerType;
        private Type _edgeBarConfigType;
        private Type _edgeBarSideType;
        private PropertyInfo _valueProperty;
        private long _controllerRetryListener;
        private bool _disposed;
        private bool _barRegistered;
        private StaminaSnapshot _lastSnapshot;

        public HudUiVigorBridge(ICoreClientAPI capi, HudVigorBar vigorHud)
        {
            _capi = capi;
            _vigorHud = vigorHud;
            _snapshotHandler = OnDisplayStaminaChanged;

            _vigorHud.DisplayStaminaChanged += _snapshotHandler;
            TryInitialize();
        }

        private void TryInitialize()
        {
            if (_disposed || _valueNotifier != null)
            {
                return;
            }

            _controllerType = FindType("HudUI.Widgets.EdgeBarController");
            _edgeBarConfigType = FindType("HudUI.Widgets.EdgeBarConfig");
            _edgeBarSideType = FindType("HudUI.Widgets.EdgeBarSide");
            Type notifierOpenType = FindType("Gui.Widgets.Framework.ValueNotifier`1");

            if (_controllerType == null || _edgeBarConfigType == null || _edgeBarSideType == null || notifierOpenType == null)
            {
                ScheduleControllerRetry();
                return;
            }

            Type notifierType = notifierOpenType.MakeGenericType(typeof(float));
            _valueNotifier = Activator.CreateInstance(notifierType, 1f);
            _displayValueNotifier = Activator.CreateInstance(notifierType, 0f);
            _valueProperty = notifierType.GetProperty("Value");

            UpdateNotifierValues(_lastSnapshot);
            TryRegisterBar();
        }

        private void ScheduleControllerRetry()
        {
            if (_controllerRetryListener != 0)
            {
                return;
            }

            _controllerRetryListener = _capi.Event.RegisterGameTickListener(_ =>
            {
                if (_disposed)
                {
                    return;
                }

                TryInitialize();
                TryRegisterBar();

                if (_barRegistered && _controllerRetryListener != 0)
                {
                    _capi.Event.UnregisterGameTickListener(_controllerRetryListener);
                    _controllerRetryListener = 0;
                }
            }, 250);
        }

        private void TryRegisterBar()
        {
            object controller = GetController();
            if (_disposed || _barRegistered || controller == null || _valueNotifier == null)
            {
                ScheduleControllerRetry();
                return;
            }

            MethodInfo hasBar = _controllerType.GetMethod("HasBar", new[] { typeof(string) });
            bool alreadyRegistered = hasBar != null && (bool)hasBar.Invoke(controller, new object[] { BarId });
            if (alreadyRegistered)
            {
                _barRegistered = true;
                return;
            }

            object side = Enum.Parse(_edgeBarSideType, "Right");
            object config = CreateEdgeBarConfig();
            MethodInfo addBar = _controllerType.GetMethods()
                .FirstOrDefault(method => method.Name == "AddBar" && method.GetParameters().Length >= 3);

            addBar?.Invoke(controller, new[] { BarId, side, config, null });
            _barRegistered = addBar != null;
        }

        private object CreateEdgeBarConfig()
        {
            ConstructorInfo constructor = _edgeBarConfigType.GetConstructors()
                .OrderByDescending(ctor => ctor.GetParameters().Length)
                .First();
            ParameterInfo[] parameters = constructor.GetParameters();
            object[] args = new object[parameters.Length];

            for (int i = 0; i < parameters.Length; i++)
            {
                ParameterInfo parameter = parameters[i];
                args[i] = parameter.Name switch
                {
                    "color" => CreateVector4FromHex(VigorModSystem.Instance.CurrentConfig.HorizontalStatusBarColorHex),
                    "valueSource" => _valueNotifier,
                    "flashThreshold" => 0.2f,
                    "displayValueSource" => _displayValueNotifier,
                    "iconDomain" => "vigor",
                    "iconPath" => "textures/hud/stamina.svg",
                    _ => parameter.HasDefaultValue ? parameter.DefaultValue : GetDefault(parameter.ParameterType)
                };
            }

            return constructor.Invoke(args);
        }

        private object CreateVector4FromHex(string hex)
        {
            float r = 0.91f;
            float g = 0.62f;
            float b = 0.18f;

            if (!string.IsNullOrWhiteSpace(hex))
            {
                string clean = hex.Trim().TrimStart('#');
                if (clean.Length == 6 &&
                    int.TryParse(clean.Substring(0, 2), System.Globalization.NumberStyles.HexNumber, null, out int ri) &&
                    int.TryParse(clean.Substring(2, 2), System.Globalization.NumberStyles.HexNumber, null, out int gi) &&
                    int.TryParse(clean.Substring(4, 2), System.Globalization.NumberStyles.HexNumber, null, out int bi))
                {
                    r = ri / 255f;
                    g = gi / 255f;
                    b = bi / 255f;
                }
            }

            Type vectorType = _edgeBarConfigType.GetConstructors()
                .SelectMany(ctor => ctor.GetParameters())
                .First(parameter => parameter.Name == "color")
                .ParameterType;

            return Activator.CreateInstance(vectorType, r, g, b, 1f);
        }

        private void OnDisplayStaminaChanged(StaminaSnapshot snapshot)
        {
            _lastSnapshot = snapshot;
            UpdateNotifierValues(snapshot);
        }

        private void UpdateNotifierValues(StaminaSnapshot snapshot)
        {
            if (_valueProperty == null || _valueNotifier == null || _displayValueNotifier == null)
            {
                return;
            }

            float max = Math.Max(1f, snapshot.MaxStamina);
            float stamina = Math.Clamp(snapshot.Stamina, 0f, max);
            _valueProperty.SetValue(_valueNotifier, stamina / max);
            _valueProperty.SetValue(_displayValueNotifier, stamina);
        }

        private object GetController()
        {
            return _controllerType?.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
        }

        private static Type FindType(string fullName)
        {
            return AppDomain.CurrentDomain.GetAssemblies()
                .Select(assembly => assembly.GetType(fullName, throwOnError: false))
                .FirstOrDefault(type => type != null);
        }

        private static object GetDefault(Type type)
        {
            return type.IsValueType ? Activator.CreateInstance(type) : null;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _vigorHud.DisplayStaminaChanged -= _snapshotHandler;

            if (_controllerRetryListener != 0)
            {
                _capi.Event.UnregisterGameTickListener(_controllerRetryListener);
                _controllerRetryListener = 0;
            }

            object controller = GetController();
            if (_barRegistered && controller != null)
            {
                _controllerType.GetMethod("RemoveBar", new[] { typeof(string) })?.Invoke(controller, new object[] { BarId });
            }

            (_valueNotifier as IDisposable)?.Dispose();
            (_displayValueNotifier as IDisposable)?.Dispose();
            _valueNotifier = null;
            _displayValueNotifier = null;
        }
    }
}
