﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Reflection;
using UnityEngine;
using System.Linq.Expressions;
using System.Collections;
using System.Text.RegularExpressions;

namespace KSPAPIExtensions.PartMessage
{
    /// <summary>
    /// Apply this attribute to any method you wish to recieve messages. 
    /// </summary>
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = true, Inherited = true)]
    public class PartMessageListener : Attribute
    {
        public PartMessageListener(Type message, PartRelationship relations = PartRelationship.Self, GameSceneFilter scenes = GameSceneFilter.Any)
        {
            if (message == null || !message.IsSubclassOf(typeof(Delegate)))
                throw new ArgumentException("Message is not a delegate type");
            if (message.GetCustomAttributes(typeof(PartMessage), true).Length == 0)
                throw new ArgumentException("Message does not have the PartMessage attribute");

            this.message = message;
            this.scenes = scenes;
            this.relations = relations;
        }

        /// <summary>
        /// The delegate type that we are listening for.
        /// </summary>
        public readonly Type message;
        /// <summary>
        /// Scene to listen for messageName in. Defaults to All.
        /// </summary>
        public readonly GameSceneFilter scenes;
        /// <summary>
        /// Filter for relation between the sender and the reciever.
        /// </summary>
        public readonly PartRelationship relations;
    }

    /// <summary>
    /// Marker attribute to apply to events using part messages.
    /// </summary>
    [AttributeUsage(AttributeTargets.Event)]
    public class PartMessageEvent : Attribute
    {
    }

    /// <summary>
    /// The attribute to be applied to a delegate to mark it as a PartMessage type.
    /// 
    /// To use the message, define an event within a Part or PartModule that uses this delegate.
    /// </summary>
    [AttributeUsage(AttributeTargets.Delegate)]
    public class PartMessage : Attribute
    {
        public PartMessage(Type parent = null, bool isAbstract = false)
        {
            if (parent != null)
            {
                if (!parent.IsSubclassOf(typeof(Delegate)))
                    throw new ArgumentException("Parent is not a delegate type");
                if (parent.GetCustomAttributes(typeof(PartMessage), true).Length != 1)
                    throw new ArgumentException("Parent does not have the PartMessage attribute");
            }
            this.parent = parent;
            this.isAbstract = isAbstract;
        }

        /// <summary>
        /// Often there is a heirachy of events - with more specific events and encompasing general events.
        /// Define a general event as the parent in this instance and any listeners to the general event
        /// will also be notified. Note that the arguments in this situation are expected to be a truncation
        /// of the argument list for this event.
        /// </summary>
        readonly public Type parent;

        /// <summary>
        /// This event is considered abstract - it should not be send directly but should be sent from one of the child events.
        /// </summary>
        readonly bool isAbstract;
    }

    /// <summary>
    /// Interface to implement on things that aren't either Parts or PartModules to enable them to send/recieve messages
    /// using the event system as a proxy for an actual part. This interface is not required, however if not implemented
    /// the part relationship filter will not work.
    /// You will need to call PartMessageService.ScanObject(object) in the Awake method.
    /// </summary>
    public interface PartMessagePartProxy
    {
        Part proxyPart { get; }
    }


    /// <summary>
    /// PartMessageListeners can use the properties in this class to examine details about the current message being
    /// handled
    /// </summary>
    public class PartMessageSourceInfo
    {
        internal PartMessageSourceInfo() { }

        /// <summary>
        /// The message type
        /// </summary>
        public Type message
        {
            get { return curr.Peek().message; }
        }

        /// <summary>
        /// All messages this represents, from most specific to most general
        /// </summary>
        public IEnumerable<Type> allMessages
        {
            get
            {
                return curr.Peek();
            }
        }

        /// <summary>
        /// The source of the event
        /// </summary>
        public object source
        {
            get
            {
                return curr.Peek().source;
            }
        }

        /// <summary>
        /// The source part. This may be null if the source was not a Part or PartModule
        /// </summary>
        public Part srcPart
        {
            get
            {
                object src = source;
                if (src is Part)
                    return (Part)src;
                if (src is PartModule)
                    return ((PartModule)src).part;
                if (src is PartMessagePartProxy)
                    return ((PartMessagePartProxy)src).proxyPart;
                return null;
            }
        }

        /// <summary>
        /// The source PartModule. This will be null if the source was not a PartModule
        /// </summary>
        public PartModule srcModule
        {
            get { return source as PartModule; }
        }

        /// <summary>
        /// Find relationship between the message source and the specified part.
        /// </summary>
        /// <param name="destPart"></param>
        /// <returns>The relationship. This will be PartRelationship.Unknown if the dest part is null.</returns>
        public PartRelationship SourceRelationTo(Part destPart)
        {
            Part src = srcPart;
            if (src == null)
                return PartRelationship.Unknown;
            return src.RelationTo(destPart);
        }

        #region Internal Bits
        private Stack<Info> curr = new Stack<Info>();

        internal IDisposable Push(object source, Type message)
        {
            return new Info(this, source, message);
        }

        private class Info : MessageEnumerable, IDisposable
        {
            internal Info(PartMessageSourceInfo info, object source, Type message)
                : base(message)
            {
                this.source = source;
                this.info = info;
                info.curr.Push(this);
            }

            readonly internal PartMessageSourceInfo info;
            readonly internal object source;

            void IDisposable.Dispose()
            {
                info.curr.Pop();
            }

        }
        #endregion
    }

    #region Delegates for some standard events

    /// <summary>
    /// Listen for this to get notification when any physical constant is changed
    /// including the mass, CoM, moments of inertia, boyancy, ect.
    /// </summary>
    [PartMessage(isAbstract: true)]
    public delegate void PartPhysicsChanged();

    /// <summary>
    /// Message for when the part's mass is modified.
    /// </summary>
    [PartMessage(typeof(PartPhysicsChanged))]
    public delegate void PartMassChanged();

    /// <summary>
    /// Message for when the part's CoMOffset changes.
    /// </summary>
    [PartMessage(typeof(PartPhysicsChanged))]
    public delegate void PartCoMOffsetChanged();

    /// <summary>
    /// Message for when the part's moments of intertia change.
    /// </summary>
    [PartMessage(typeof(PartPhysicsChanged))]
    public delegate void PartMomentsChanged();


    /// <summary>
    /// Message for when the part's resource list is modified in some way.
    /// </summary>
    [PartMessage(isAbstract: true)]
    public delegate void PartResourcesChanged();

    /// <summary>
    /// Message for when the part's resource list is modified (added to or subtracted from).
    /// </summary>
    [PartMessage(typeof(PartResourcesChanged))]
    public delegate void PartResourceListChanged();

    /// <summary>
    /// Message for when the max amount of a resource is modified.
    /// </summary>
    [PartMessage(typeof(PartResourcesChanged))]
    public delegate void PartResourceMaxAmountChanged(PartResource resource);

    /// <summary>
    /// Message for when the initial amount of a resource is modified (only raised in the editor)
    /// </summary>
    [PartMessage(typeof(PartResourcesChanged))]
    public delegate void PartResourceInitialAmountChanged(PartResource resource);

    /// <summary>
    /// Message for when some change has been made to the part's rendering model.
    /// </summary>
    [PartMessage]
    public delegate void PartModelChanged();

    /// <summary>
    /// Message for when some change has been made to the part's collider.
    /// </summary>
    [PartMessage]
    public delegate void PartColliderChanged();
    #endregion

    /// <summary>
    /// A filter method for outgoing messages. This is called prior to delivery of any messages. If the method returns true
    /// then the messageName is considered handled and will not be delivered.
    /// 
    /// Information about the source of the messageName is avaiable from the PartMessageSourceInfo as usual.
    /// </summary>
    /// <param name="source">The class where the event is defined</param>
    /// <param name="message">The message being sent.</param>
    /// <param name="args">Arguments to the event.</param>
    /// <returns>True if the message is considered handled and is not to be delivered.</returns>
    public delegate bool PartMessageFilter(object source, Type message, object [] args);

    [KSPAddonFixed(KSPAddon.Startup.Instantly, false, typeof(PartMessageService))]
    public class PartMessageService : MonoBehaviour
    {
        // The singleton instance of the service.
        public static PartMessageService Instance
        {
            get;
            private set;
        }

        public static PartMessageSourceInfo SourceInfo
        {
            get;
            private set;
        }

        #region Object scanning

        /// <summary>
        /// Scan an object for messageName events and messageName listeners and hook them up.
        /// Note that all references are dumped on game scene change, so objects must be rescanned when reloaded.
        /// </summary>
        /// <param name="obj">the object to scan</param>
        public void ScanObject(object obj)
        {
            if (obj is PartModule)
                ScanModule((PartModule)obj);
            else if (obj is Part)
                ScanPart((Part)obj);
            else
                ScanObjectInternal(obj);
        }

        /// <summary>
        /// Scan a module for messageName events and listeners. 
        /// </summary>
        /// <param name="module"></param>
        public void ScanModule(PartModule module)
        {
            if (!NeedsManager(module.GetType()))
                return;

            PartMessageManager manager = module.GetComponent<PartMessageManager>();
            if (manager == null)
            {
                if (!NeedsManager(module.GetType()))
                    return;
                manager = module.gameObject.AddComponent<PartMessageManager>();
                ScanObjectInternal(module.part);
            }
                
            if (manager.modulesScanned.Contains(module))
                return;

            ScanObjectInternal((object)module);
            manager.modulesScanned.Add(module);
        }

        public void ScanPart(Part part)
        {
            PartMessageManager manager = part.GetComponent<PartMessageManager>();
            if (manager != null)
                return;
            manager = part.gameObject.AddComponent<PartMessageManager>();
            ScanPartInternal(part, manager);
        }

        #region Internal Bits
        internal void ScanPartInternal(Part part, PartMessageManager manager)
        {
            ScanObjectInternal(part);
            foreach (PartModule module in part.Modules)
            {
                if (module == manager)
                    continue;

                ScanModuleInternal(module, manager);
            }
        }

        internal void ScanModuleInternal(PartModule module, PartMessageManager manager)
        {
            ScanObjectInternal(module);
            manager.modulesScanned.Add(module);
        }

        private void ScanObjectInternal(object obj)
        {
            Type t = obj.GetType();

            foreach (MethodInfo meth in t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
            {
                foreach (PartMessageListener attr in meth.GetCustomAttributes(typeof(PartMessageListener), true))
                    AddListener(obj, meth, attr);
            }

            foreach (EventInfo evt in t.GetEvents(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
            {
                Type deleg = evt.EventHandlerType;
                if (evt.GetCustomAttributes(typeof(PartMessageEvent), true).Length == 0)
                {
                    // sanity check
                    if (deleg.GetCustomAttributes(typeof(PartMessage), true).Length > 0)
                        Debug.LogWarning(string.Format("[PartMessageManager] Event: {0} in class: {1} declares an event with a part message, but does not have the PartMessageEvent attribute. Will ignore", evt.Name, t.FullName));
                    continue;
                }
                foreach (PartMessage attr in deleg.GetCustomAttributes(typeof(PartMessage), true))
                    GenerateEventHandoff(obj, evt);
            }
        }

        internal class ListenerInfo
        {
            public WeakReference targetRef;
            public MethodInfo method;
            public PartMessageListener attr;

            public LinkedListNode<ListenerInfo> node;

            public object target
            {
                get
                {
                    return targetRef.Target;
                }
            }

            public Part part
            {
                get
                {
                    object target = this.target;
                    if (target is Part)
                        return target as Part;
                    if (target is PartModule)
                        return ((PartModule)target).part;
                    if (target is PartMessagePartProxy)
                        return ((PartMessagePartProxy)target).proxyPart;
                    return null;
                }
            }

            public PartModule module
            {
                get
                {
                    return target as PartModule;
                }
            }
        }

        internal class FilterInfo : IDisposable
        {
            public object source;
            public PartMessageFilter Filter;
            public HashSet<string> messages = new HashSet<string>();

            public LinkedListNode<FilterInfo> node;

            public virtual void Dispose()
            {
                node.List.Remove(node);
            }
        }

        internal class MessageConsolidator : FilterInfo
        {
            public MessageConsolidator(PartMessageService service) 
            {
                this.service = service;
                this.Filter = PartMessageFilter;
            }

            private PartMessageService service;

            private class PartMessageInfo : IEquatable<PartMessageInfo>
            {
                public object source;
                public Type message;
                public object[] args;

                private readonly int hashCode;

                public PartMessageInfo(object source, Type message, object[] args)
                {
                    this.source = source;
                    this.message = message;
                    this.args = args;
                    hashCode = source.GetHashCode() ^ message.FullName.GetHashCode() ^ args.Length;
                    foreach (object arg in args)
                        hashCode ^= arg.GetHashCode();
                }

                public override bool Equals(object obj)
                {
                    return base.Equals(obj as PartMessageInfo);
                }

                public bool Equals(PartMessageInfo other)
                {
                    if (other == null)
                        return false;
                    if (other == this)
                        return true;

                    if (hashCode != other.hashCode)
                        return false;
                    if (source != other.source)
                        return false;
                    if (message.FullName != other.message.FullName)
                        return false;
                    if (args.Length != other.args.Length)
                        return false;
                    for (int i = 0; i < args.Length; i++)
                        if (!args[i].Equals(other.args[i]))
                            return false;
                    return true;
                }

                public override int GetHashCode()
                {
                    return hashCode;
                }
            }

            private HashSet<PartMessageInfo> messageSet = new HashSet<PartMessageInfo>();
            private List<PartMessageInfo> messageList = new List<PartMessageInfo>();

            private bool PartMessageFilter(object source, Type message, object[] args)
            {
                var info = new PartMessageInfo(source, message, args);
                if(messageSet.Add(info))
                    messageList.Add(info);
                return true;
            }

            public override void Dispose()
            {
                base.Dispose();

                foreach (PartMessageInfo message in messageList)
                    service.SendPartMessage(message.source, message.message, message.args);

                messageSet = null;
                messageList = null;
            }

        }

        internal void AddListener(object target, MethodInfo meth, PartMessageListener attr)
        {
            //Debug.LogWarning(string.Format("[PartMessageUtils] {0}.{1} Adding other for {2}", target.GetType().Name, meth.Name, attr.messageName.FullName));

            if (!attr.scenes.IsLoaded())
                return;

            string message = attr.message.FullName;
            if (Delegate.CreateDelegate(attr.message, target, meth, false) == null)
            {
                Debug.LogError(string.Format("PartMessageListener method {0}.{1} does not support the delegate type {2} as declared in the attribute", meth.DeclaringType, meth.Name, attr.message.Name));
                return;
            }

            LinkedList<ListenerInfo> listenerList;
            if (!listeners.TryGetValue(message, out listenerList))
            {
                listenerList = new LinkedList<ListenerInfo>();
                listeners.Add(message, listenerList);
            }

            ListenerInfo info = new ListenerInfo();
            info.targetRef = new WeakReference(target);
            info.method = meth;
            info.attr = attr;
            info.node = listenerList.AddLast(info);
        }

        private void GenerateEventHandoff(object source, EventInfo evt)
        {
            MethodAttributes addAttrs = evt.GetAddMethod(true).Attributes;

            // This generates a dynamic method that pulls the properties of the event
            // plus the arguments passed and hands it off to the EventHandler method below.
            Type message = evt.EventHandlerType;
            MethodInfo m = message.GetMethod("Invoke", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

            //Debug.LogWarning(string.Format("[PartMessageUtils] {0}.{1} Adding event handler for {2}", source.GetType().Name, evt.Name, messageName.FullName));
            
            ParameterInfo[] pLst = m.GetParameters();
            ParameterExpression[] peLst = new ParameterExpression[pLst.Length];
            Expression[] cvrt = new Expression[pLst.Length];
            for (int i = 0; i < pLst.Length; i++)
            {
                peLst[i] = Expression.Parameter(pLst[i].ParameterType, pLst[i].Name);
                cvrt[i] = Expression.Convert(peLst[i], typeof(object));
            }
            Expression createArr = Expression.NewArrayInit(typeof(object), cvrt);

            Expression invoke = Expression.Call(Expression.Constant(this), GetType().GetMethod("SendMessageInternal", BindingFlags.NonPublic | BindingFlags.Instance),
                Expression.Constant(source), Expression.Constant(message), createArr);
            
            Delegate d = Expression.Lambda(message, invoke, peLst).Compile();

            // Shouldn't need to use a weak delegate here.
            evt.AddEventHandler(source, d);
        }
        #endregion
        #endregion

        #region Message Sending and Filtering

        /// <summary>
        /// Send a message. Normally this will be automatically invoked by the event, but there are types when dynamic invocation is required.
        /// </summary>
        /// <param name="source">Source of the message. This should be either a Part or a PartModule.</param>
        /// <param name="message">The message delegate type. This must have the PartMessage attribute.</param>
        /// <param name="args">message arguments</param>
        public void SendPartMessage(object source, Type message, params object[] args)
        {
            if (message.GetCustomAttributes(typeof(PartMessage), true).Length == 0)
                throw new ArgumentException("Message does not have PartMessage attribute", "message");

            SendMessageInternal(source, message, args);
        }

        /// <summary>
        /// Register a message filter. This delegate will be called for every message sent from the source.
        /// If it returns true, the message is considered handled and no futher processing will occour.
        /// </summary>
        /// <param name="filter">The delegate for the filter</param>
        /// <param name="source">Message source, must match. If null will match all sources.</param>
        /// <param name="messages">Optional list of messages to match. If empty, all messages are matched.</param>
        /// <returns>Disposable object. When done call dispose. Works well with using clauses.</returns>
        public IDisposable MessageFilter(PartMessageFilter filter, object source, params Type[] messages)
        {
            FilterInfo info = new FilterInfo();
            info.Filter = filter;

            RegisterFilterInfo(source, messages, info);

            return info;
        }

        /// <summary>
        /// Consolidate messages. All messages sent by the source will be held until the returned object is destroyed.
        /// Any duplicates of the same message will be swallowed silently.
        /// </summary>
        /// <param name="source">source to consolidate from. Null will match all sources</param>
        /// <param name="messages">messages to consolidate. If not specified, all messages are consolidated.</param>
        /// <returns>Disposable object. When done call dispose. Works well with using clauses.</returns>
        public IDisposable MessageConsolidate(object source, params Type[] messages)
        {
            FilterInfo consolidator = new MessageConsolidator(this);

            RegisterFilterInfo(source, messages, consolidator);

            return consolidator;
        }

        /// <summary>
        /// Ignore messages sent by the source until the returned object is destroyed.
        /// </summary>
        /// <param name="source">Source to ignore. Null will ignore all sources.</param>
        /// <param name="messages">Messages to ignore. If not specified, all messages are ignored.</param>
        /// <returns>Disposable object. When done call dispose. Works well with using clauses.</returns>
        public IDisposable MessageIgnore(object source, params Type[] messages)
        {
            return MessageFilter((src,message,args)=>true, source, messages);
        }

        #region Internal Bits
        private Dictionary<string, LinkedList<ListenerInfo>> listeners = new Dictionary<string, LinkedList<ListenerInfo>>();
        private LinkedList<FilterInfo> filters = new LinkedList<FilterInfo>();

        private void RegisterFilterInfo(object source, Type[] messages, FilterInfo info)
        {
            info.source = source;

            foreach (Type root in messages)
                foreach (Type message in new MessageEnumerable(root))
                    info.messages.Add(message.FullName);

            info.node = filters.AddFirst(info);
        }

        private void SendMessageInternal(object source, Type message, object[] args)
        {
            //Debug.LogWarning(string.Format("[PartMessageUtils] {0}.{1} Event invoked", source.GetType().Name, evt.Name));
            
            using (SourceInfo.Push(source, message))
            {

                foreach (FilterInfo filter in filters)
                    if ((filter.source == null || filter.source == source)
                        && (filter.messages.Count == 0 || filter.messages.Contains(message.FullName)) 
                        && filter.Filter(source, message, args))
                        return;

                // Send the message
                foreach (Type messageCls in SourceInfo.allMessages)
                {
                    string messageName = messageCls.FullName;

                    LinkedList<ListenerInfo> listenerList;
                    if (!listeners.TryGetValue(messageName, out listenerList))
                        continue;

                    // Shorten parameter list if required
                    object[] newArgs = null;

                    for (var node = listenerList.First; node != null; )
                    {
                        // hold reference for duration of call
                        ListenerInfo info = node.Value;
                        object target = info.target;
                        if (target == null)
                        {
                            // Remove dead links from the list
                            var tmp = node;
                            node = node.Next;
                            listenerList.Remove(tmp);
                            continue;
                        }

                        // Declarative event filtering
                        PartModule module = info.module;
                        if ((module == null || (module.isEnabled && module.enabled))
                            && info.attr.scenes.IsLoaded()
                            && PartUtils.RelationTest(SourceInfo.srcPart, info.part, info.attr.relations)) 
                        {
                            try
                            {
                                node.Value.method.Invoke(target, newArgs ?? (newArgs = ShortenArgs(args, messageCls)));
                            }
                            catch (TargetException ex)
                            {
                                // Swallow target exceptions, but not anything else.
                                Debug.LogError(string.Format("Invoking {0}.{1} to handle messageName {2} resulted in an exception.", target.GetType(), node.Value.method, SourceInfo.message));
                                Debug.LogException(ex.InnerException);
                            }
                        }

                        node = node.Next;
                    }

                }
            }
        }

        private static object[] ShortenArgs(object[] args, Type messageCls)
        {
            ParameterInfo[] methodParams = messageCls.GetMethod("Invoke").GetParameters();
            object[] newArgs = args;
            if (args.Length > methodParams.Length)
            {
                newArgs = new object[methodParams.Length];
                Array.Copy(args, newArgs, methodParams.Length);
            }
            return newArgs;
        }
        #endregion
        #endregion

        #region Internal - Startup and instance management

        protected PartMessageService() { }

        internal void Update()
        {
            // Use the Update method to be sure this runs *after* module manager. (MM used OnGUI)
            if (!GameDatabase.Instance.IsReady() && ((HighLogic.LoadedScene == GameScenes.MAINMENU) || (HighLogic.LoadedScene == GameScenes.SPACECENTER)))
            {
                return;
            }

            // Clear the enabled flag so we don't end up with lots of invocations.
            enabled = false;
            
            var allTypes = getAllTypes();

            var eligible = from t in allTypes
                           where t.FullName == this.GetType().FullName
                           select t.Assembly;

            Assembly currentAssembly = Assembly.GetExecutingAssembly();

            // Elect the newest loaded version of MM to process all patch files.
            // If there is a newer version loaded then don't do anything
            if (eligible.Any(a => a.GetName().Version.CompareTo(currentAssembly.GetName().Version) == 1 || a.Location.CompareTo(currentAssembly.Location) > 0))
            {
                print("[PartMessageService] version " + currentAssembly.GetName().Version + " at " + currentAssembly.Location + " lost the election");
                return;
            }
            else
            {
                string candidates = "";
                foreach (Assembly a in eligible)
                    if (currentAssembly.Location != a.Location)
                        candidates += "Version " + a.GetName().Version + " " + a.Location + " " + "\n";
                if (candidates.Length > 0)
                    Debug.Log("[PartMessageService] version " + currentAssembly.GetName().Version + " at " + currentAssembly.Location + " won the election against\n" + candidates);
                else
                    Debug.Log("[PartMessageService] Elected unopposed version= " + currentAssembly.GetName().Version + " at " + currentAssembly.Location);
            }

            if (Instance != null)
            {
                Debug.Log("[PartMessageService] destroying from previous load");
                UnityEngine.Object.Destroy(Instance.gameObject);
            }

            // Process the database
            UnityEngine.Object.DontDestroyOnLoad(gameObject);
            Instance = this;
            SourceInfo = new PartMessageSourceInfo();

            // Clear the listeners list when reloaded.
            GameEvents.onGameSceneLoadRequested.Add(scene => listeners.Clear());

            var parts = (from t in allTypes
                         where typeof(Part).IsAssignableFrom(t)
                         select t).ToLookup(t => t.Name);
            
            var modules = (from t in allTypes
                           where typeof(PartModule).IsAssignableFrom(t)
                           select t).ToLookup(t => t.Name);

            var assem = AssemblyLoader.loadedAssemblies.ToDictionary(a => a.assembly.GetName().Name);

            foreach (UrlDir.UrlConfig urlConf in GameDatabase.Instance.root.AllConfigs)
            {
                if (urlConf.type != "PART")
                    continue;

                ConfigNode part = urlConf.config;
                string partName = part.GetValue("name");

                foreach (string keyValue in part.GetValues("RequiresAssembly"))
                {
                    foreach (string split in keyValue.Split(','))
                    {
                        string value = split.Trim();
                        if (!string.IsNullOrEmpty(value) && (value[0] == '!' ? assem.ContainsKey(value.Substring(1).Trim()) : !assem.ContainsKey(value)))
                            goto doHide;
                    }
                }

                goto noHide;
                
            doHide:
                Debug.Log("[PartMessageService] removing part " + partName + " due to dependency requirements not met");
                part.ClearData();
                
            noHide:
                part.RemoveValues("RequiresAssembly");

                string partModule = part.GetValue("module");
                Type partType = parts[partModule].FirstOrDefault();

                if (partType == null)
                    continue;

                if (NeedsManager(partType))
                    goto addmanager;

                foreach (ConfigNode module in part.GetNodes("MODULE"))
                {
                    string moduleName = module.GetValue("name");
                    Type moduleType = modules[moduleName].FirstOrDefault();

                    if (moduleType == null)
                        continue;

                    if (NeedsManager(moduleType))
                        goto addmanager;
                }
                continue;

            addmanager:

                Debug.Log("[PartMessageService] Adding support for part messages to part " + part.GetValue("name"));

                try
                {
                    ConfigNode myModule = new ConfigNode("MODULE");
                    myModule.AddValue("name", typeof(PartMessageBootstrap).Name);
                    part.AddNode(myModule);
                }
                catch (Exception ex)
                {
                    Debug.LogException(ex);
                }

            }
        }

        private static bool NeedsManager(Type t)
        {
            foreach (EventInfo info in t.GetEvents(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
                if (info.GetCustomAttributes(typeof(PartMessageEvent), true).Length > 0)
                    return true;

            foreach (MethodInfo meth in t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
                if (meth.GetCustomAttributes(typeof(PartMessageListener), true).Length > 0)
                    return true;

            return false;
        }

        private static IEnumerable<Type> getAllTypes()
        {
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type[] types;
                try
                {
                    types = assembly.GetTypes();
                }
                catch (Exception)
                {
                    types = Type.EmptyTypes;
                }

                foreach (var type in types)
                {
                    yield return type;
                }
            }
        }
        #endregion

    }

    #region Implementation
    internal class MessageEnumerable : IEnumerable<Type>
    {
        internal MessageEnumerable(Type message)
        {
            this.message = message;
        }

        readonly internal Type message;

        IEnumerator<Type> IEnumerable<Type>.GetEnumerator()
        {
            return new MessageEnumerator(message);
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return new MessageEnumerator(message);
        }

        private class MessageEnumerator : IEnumerator<Type>
        {

            public MessageEnumerator(Type top)
            {
                this.current = this.top = top;
            }

            private int pos = -1;
            private Type current;
            private Type top;

            object IEnumerator.Current
            {
                get
                {
                    if (pos != 0)
                        throw new InvalidOperationException();
                    return current;
                }
            }

            Type IEnumerator<Type>.Current
            {
                get
                {
                    if (pos != 0)
                        throw new InvalidOperationException();
                    return current;
                }
            }

            bool IEnumerator.MoveNext()
            {
                switch (pos)
                {
                    case -1:
                        current = top;
                        pos = 0;
                        break;
                    case 1:
                        return false;
                    case 0:
                        PartMessage evt = (PartMessage)current.GetCustomAttributes(typeof(PartMessage), true)[0];
                        current = evt.parent;
                        break;
                    case 2:
                        throw new InvalidOperationException("Enumerator disposed");
                }
                if (current == null)
                {
                    pos = 1;
                    return false;
                }
                return true;
            }

            void IEnumerator.Reset()
            {
                pos = -1;
                current = null;
            }

            void IDisposable.Dispose()
            {
                current = top = null;
                pos = 2;
            }
        }
    }

    public class PartMessageBootstrap : PartModule
    {
        public override void OnAwake()
        {
            if (gameObject.GetComponent<PartMessageManager>() == null)
                gameObject.AddComponent<PartMessageManager>();

            if (GameSceneFilter.AnyEditor.IsLoaded())
                part.RemoveModule(this);
        }

        public override void OnLoad(ConfigNode node)
        {
            // So hopefully this is the last module in the list, and we can successfully add the
            // manager here. The only case (major edge case) where this wouldn't work would be 
            // if another KSPAddon adds in a different module at the end of the list, and then
            // uses part messages in the GetInfo method. 

            if (gameObject.GetComponent<PartMessageManager>() == null)
                gameObject.AddComponent<PartMessageManager>();

            if (GameSceneFilter.Flight.IsLoaded())
                part.RemoveModule(this);
        }

        public override string GetInfo()
        {
            // There's a chance the manager was not last on the list. If it 
            List<PartModule> partModuleList = (List<PartModule>)moduleListList.GetValue(part.Modules);
            if (partModuleList[partModuleList.Count - 1] != this)
            {
                PartMessageService svc = PartMessageService.Instance;
                PartMessageManager manager = gameObject.GetComponent<PartMessageManager>();
                // Just scan any modules that have been put in after this one.
                for (int i = partModuleList.IndexOf(this) + 1; i < partModuleList.Count; ++i)
                {
                    svc.ScanModuleInternal(partModuleList[i], manager);
                }
            }

            return string.Empty;
        }

        private static FieldInfo moduleListList = (from fld in typeof(PartModuleList).GetFields(BindingFlags.NonPublic | BindingFlags.Instance)
                                                   where fld.FieldType == typeof(List<PartModule>)
                                                   select fld).First();
    }

    public class PartMessageManager : MonoBehaviour
    {
        public void Awake()
        {
            Part part = gameObject.GetComponent<Part>();
            // No part in editor icons. Might as well remove this behaviour.
            if (part == null)
            {
                DestroyObject(this);
                return;
            }
            PartMessageService.Instance.ScanPartInternal(part, this);
        }

        internal List<PartModule> modulesScanned = new List<PartModule>();
    }
    #endregion


}
