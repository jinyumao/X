﻿using System;
using System.Collections.Generic;
using System.Linq;
using NewLife.Http;
using NewLife.Messaging;
using NewLife.Net;
using NewLife.Reflection;
using NewLife.Threading;

namespace NewLife.Remoting
{
    class ApiNetServer : NetServer<ApiNetSession>, IApiServer
    {
        /// <summary>主机</summary>
        public IApiHost Host { get; set; }

        /// <summary>当前服务器所有会话</summary>
        public IApiSession[] AllSessions => Sessions.ToValueArray().Where(e => e is IApiSession).Cast<IApiSession>().ToArray();

        public ApiNetServer()
        {
            Name = "Api";
            UseSession = true;
        }

        /// <summary>初始化</summary>
        /// <param name="config"></param>
        /// <param name="host"></param>
        /// <returns></returns>
        public virtual Boolean Init(Object config, IApiHost host)
        {
            Host = host;

            Local = config as NetUri;
            // 如果主机为空，监听所有端口
            if (Local.Host.IsNullOrEmpty() || Local.Host == "*") AddressFamily = System.Net.Sockets.AddressFamily.Unspecified;

            // Http封包协议
            //Add<HttpCodec>();
            Add(new HttpCodec { AllowParseHeader = true });

            // 新生命标准网络封包协议
            Add(Host.GetMessageCodec());

            return true;
        }
    }

    class ApiNetSession : NetSession<ApiNetServer>, IApiSession
    {
        private ApiServer _Host;
        /// <summary>主机</summary>
        IApiHost IApiSession.Host => _Host;

        /// <summary>最后活跃时间</summary>
        public DateTime LastActive { get; set; }

        /// <summary>所有服务器所有会话，包含自己</summary>
        public virtual IApiSession[] AllSessions => _Host.Server.AllSessions;

        /// <summary>令牌</summary>
        public String Token { get; set; }

        /// <summary>请求参数</summary>
        public IDictionary<String, Object> Parameters { get; set; }

        /// <summary>第二会话数据</summary>
        public IDictionary<String, Object> Items2 { get; set; }

        /// <summary>获取/设置 用户会话数据。优先使用第二会话数据</summary>
        /// <param name="key"></param>
        /// <returns></returns>
        public override Object this[String key]
        {
            get
            {
                var ms = Items2 ?? Items;
                if (ms.TryGetValue(key, out var rs)) return rs;

                return null;
            }
            set
            {
                var ms = Items2 ?? Items;
                ms[key] = value;
            }
        }

        /// <summary>开始会话处理</summary>
        public override void Start()
        {
            base.Start();

            _Host = Host.Host as ApiServer;
        }

        /// <summary>查找Api动作</summary>
        /// <param name="action"></param>
        /// <returns></returns>
        public virtual ApiAction FindAction(String action) => _Host.Manager.Find(action);

        /// <summary>创建控制器实例</summary>
        /// <param name="api"></param>
        /// <returns></returns>
        public virtual Object CreateController(ApiAction api)
        {
            var controller = api.Controller;
            if (controller != null) return controller;

            controller = api.Type.CreateInstance();

            return controller;
        }

        protected override void OnReceive(ReceivedEventArgs e)
        {
            LastActive = DateTime.Now;

            // Api解码消息得到Action和参数
            var msg = e.Message as IMessage;
            if (msg == null || msg.Reply) return;

            // 连接复用
            if (_Host is ApiServer svr && svr.Multiplex)
            {
                ThreadPoolX.QueueUserWorkItem(m =>
                {
                    var rs = _Host.Process(this, m);
                    if (rs != null) Session?.SendMessage(rs);
                }, msg);
            }
            else
            {
                var rs = _Host.Process(this, msg);
                if (rs != null) Session?.SendMessage(rs);
            }
        }
    }
}
