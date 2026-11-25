using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.Toolkit.Win32.UI.XamlHost;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Markup;

namespace GanttIsland
{
    /// <summary>
    /// XamlApplication initialises the UWP XAML framework on this thread and aggregates metadata providers.
    /// The Syncfusion UWP controls are managed types with no generated XamlTypeInfo, so XAML cannot resolve them
    /// ("type was not found in namespace ...") unless we supply a provider. ReflectionMetadataProvider does it.
    /// </summary>
    internal sealed class GanttXamlApp : XamlApplication
    {
        public GanttXamlApp()
        {
            // The base constructor already initialises XAML; calling Initialize() again overflows the stack.
            MetadataProviders.Add(new ReflectionMetadataProvider());
        }
    }

    internal sealed class ReflectionMetadataProvider : IXamlMetadataProvider
    {
        private readonly Dictionary<Type, IXamlType> _types = new Dictionary<Type, IXamlType>();
        private readonly List<Assembly> _assemblies = new List<Assembly>();

        public ReflectionMetadataProvider()
        {
            var dir = Path.GetDirectoryName(typeof(ReflectionMetadataProvider).Assembly.Location);
            foreach (var file in Directory.GetFiles(dir, "Syncfusion.*.UWP.dll"))
                _assemblies.Add(Assembly.LoadFrom(file));
        }

        public IXamlType GetXamlType(Type type)
        {
            Trace("GetXamlType(Type) " + type.FullName);
            // Only claim Syncfusion types; everything else is resolved by the XAML framework itself.
            return IsOurs(type) ? Wrap(type) : null;
        }

        public IXamlType GetXamlType(string fullName)
        {
            Trace("GetXamlType(string) " + fullName);
            foreach (var asm in _assemblies)
            {
                var t = asm.GetType(fullName, false);
                if (t != null) return Wrap(t);
            }
            return null;
        }

        internal static void Trace(string m) { try { File.AppendAllText(Path.Combine(Path.GetDirectoryName(typeof(ReflectionMetadataProvider).Assembly.Location), "provider.log"), m + Environment.NewLine); } catch { } }

        public XmlnsDefinition[] GetXmlnsDefinitions() => new XmlnsDefinition[0];

        private static bool IsOurs(Type t) => t.Assembly.GetName().Name.StartsWith("Syncfusion.", StringComparison.Ordinal);

        internal IXamlType Wrap(Type type)
        {
            if (type == null) return null;
            if (!_types.TryGetValue(type, out var xt)) _types[type] = xt = new ReflectionXamlType(this, type);
            return xt;
        }
    }

    internal sealed class ReflectionXamlType : IXamlType
    {
        private readonly ReflectionMetadataProvider _p;
        private readonly Type _t;
        public ReflectionXamlType(ReflectionMetadataProvider p, Type t) { _p = p; _t = t; }

        public string FullName => _t.FullName;
        public Type UnderlyingType => _t;
        public IXamlType BaseType => _t.BaseType == null ? null : _p.Wrap(_t.BaseType);
        public IXamlType BoxedType => null;
        public bool IsArray => _t.IsArray;
        public bool IsBindable => false;
        public bool IsMarkupExtension => false;
        public bool IsDictionary => typeof(IDictionary).IsAssignableFrom(_t);
        public bool IsCollection => !IsDictionary && typeof(IList).IsAssignableFrom(_t);
        public bool IsConstructible => !_t.IsAbstract && !_t.IsInterface && !_t.IsEnum && _t.GetConstructor(Type.EmptyTypes) != null;

        public IXamlType ItemType
        {
            get
            {
                var i = _t.GetInterfaces().FirstOrDefault(x => x.IsGenericType && x.GetGenericTypeDefinition() == typeof(IList<>));
                return _p.Wrap(i != null ? i.GetGenericArguments()[0] : typeof(object));
            }
        }

        public IXamlType KeyType
        {
            get
            {
                var i = _t.GetInterfaces().FirstOrDefault(x => x.IsGenericType && x.GetGenericTypeDefinition() == typeof(IDictionary<,>));
                return _p.Wrap(i != null ? i.GetGenericArguments()[0] : typeof(object));
            }
        }

        public IXamlMember ContentProperty
        {
            get
            {
                var attr = _t.GetCustomAttributes(typeof(ContentPropertyAttribute), true).Cast<ContentPropertyAttribute>().FirstOrDefault();
                return attr == null ? null : GetMember(attr.Name);
            }
        }

        public object ActivateInstance() => Activator.CreateInstance(_t);

        public object CreateFromString(string value)
        {
            if (_t.IsEnum) return Enum.Parse(_t, value, true);
            if (_t == typeof(string)) return value;
            return Convert.ChangeType(value, _t, System.Globalization.CultureInfo.InvariantCulture);
        }

        public IXamlMember GetMember(string name)
        {
            // Attached property: static Get<Name>(DependencyObject) / Set<Name>.
            var getter = _t.GetMethod("Get" + name, BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy);
            if (getter != null && getter.GetParameters().Length == 1 &&
                _t.GetField(name + "Property", BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy) != null)
                return new ReflectionXamlMember(_p, name, _t, getter, null);

            var prop = _t.GetProperty(name, BindingFlags.Public | BindingFlags.Instance | BindingFlags.FlattenHierarchy);
            return prop == null ? null : new ReflectionXamlMember(_p, name, _t, null, prop);
        }

        public void AddToVector(object instance, object value) => ((IList)instance).Add(value);
        public void AddToMap(object instance, object key, object value) => ((IDictionary)instance).Add(key, value);
        public void RunInitializer() => System.Runtime.CompilerServices.RuntimeHelpers.RunClassConstructor(_t.TypeHandle);
    }

    internal sealed class ReflectionXamlMember : IXamlMember
    {
        private readonly ReflectionMetadataProvider _p;
        private readonly Type _owner;
        private readonly MethodInfo _attachedGet;
        private readonly PropertyInfo _prop;

        public ReflectionXamlMember(ReflectionMetadataProvider p, string name, Type owner, MethodInfo attachedGet, PropertyInfo prop)
        {
            _p = p; Name = name; _owner = owner; _attachedGet = attachedGet; _prop = prop;
        }

        public string Name { get; }
        public bool IsAttachable => _attachedGet != null;
        public bool IsDependencyProperty =>
            _owner.GetField(Name + "Property", BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy) != null;
        public bool IsReadOnly => _prop != null && !_prop.CanWrite;
        public IXamlType TargetType => _p.Wrap(_owner);
        public IXamlType Type => _p.Wrap(_attachedGet != null ? _attachedGet.ReturnType : _prop.PropertyType);

        public object GetValue(object instance) =>
            _attachedGet != null ? _attachedGet.Invoke(null, new[] { instance }) : _prop.GetValue(instance);

        public void SetValue(object instance, object value)
        {
            if (_attachedGet != null)
            {
                var setter = _owner.GetMethod("Set" + Name, BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy);
                setter.Invoke(null, new[] { instance, value });
            }
            else _prop.SetValue(instance, value);
        }
    }
}
