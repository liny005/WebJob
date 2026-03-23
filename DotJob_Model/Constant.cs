﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Host.Common
{
    public class Constant
    {
        /// <summary>
        /// 请求url RequestUrl
        /// </summary>
        public const string REQUESTURL = "RequestUrl";

        /// <summary>
        /// 请求参数 RequestParameters
        /// </summary>
        public const string REQUESTPARAMETERS = "RequestParameters";

        /// <summary>
        /// Headers（可以包含：Authorization授权认证）
        /// </summary>
        public const string HEADERS = "Headers";

        /// <summary>
        /// 是否发送邮件
        /// </summary>
        public const string MAILMESSAGE = "MailMessage";

        /// <summary>
        /// 是否钉钉通知
        /// </summary>
        public const string DINGTALK = "Dingtalk";

        /// <summary>
        /// 请求类型 RequestType
        /// </summary>
        public const string REQUESTTYPE = "RequestType";
        
        /// <summary>
        /// 执行次数
        /// </summary>
        public const string RUNNUMBER = "RunNumber";
        
        /// <summary>
        /// 执行次数限制
        /// </summary>
        public const string RUNTOTAL = "RunTotal";

        public const string MailTitle = "MailTitle";
        public const string MailContent = "MailContent";
        public const string MailTo = "MailTo";

        public const string JobTypeEnum = "JobTypeEnum";

        public const string BeginAt = "BeginAt";
        
        public const string EndAt = "EndAt";

        /// <summary>
        /// 上次执行时间（修改任务时保留）
        /// </summary>
        public const string PREVIOUSFIRETIME = "PreviousFireTime";
        
    }
}