using System.ComponentModel;
using System.Reflection;

namespace DotJob_Core.Systems;

public static class EnumExt
{
    /// <summary>
    /// 获取枚举值的Description信息
    /// </summary>
    /// <param name ="value">枚举值</param>
    /// <returns></returns>
    public static string GetDescription(this Enum value)
    {
        var field = value.GetType().GetField(value.ToString());

        var attribute = (DescriptionAttribute)field.GetCustomAttribute(typeof(DescriptionAttribute));
        if (field != null)
        {
            if (attribute != null)
            {
                return attribute.Description;
            }
        }

        return value.ToString();
    }
    
    /// <summary>
    /// 获取枚举整数值
    /// </summary>
    /// <param name="value"></param>
    /// <returns></returns>
    public static int GetIntValue(this Enum value)
    {
        return Convert.ToInt32(value);
    }
}