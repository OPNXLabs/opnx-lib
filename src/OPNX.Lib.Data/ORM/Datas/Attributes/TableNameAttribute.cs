namespace OPNX.Lib.Data.ORM.Datas.Attributes
{
    [AttributeUsage(AttributeTargets.Class)]
    public class TableNameAttribute : Attribute
    {
        public TableNameAttribute()
            :this(string.Empty)
        {
        }

        public TableNameAttribute(string name)
        {
            Name = name;
        }

        public string Name { get; set; }
    }
}
