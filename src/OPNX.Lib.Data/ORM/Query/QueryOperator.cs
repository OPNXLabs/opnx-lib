namespace OPNX.Lib.Data.ORM.Query;

public enum QueryOperator { Equal, NotEqual, GreaterThan, GreaterThanOrEqual, LessThan, LessThanOrEqual, In, NotIn, IsNull, IsNotNull, Like, NotLike, Contains, StartsWith, EndsWith, Between, NotBetween }

public enum QueryLogicalOperator { And, Or }

public enum QuerySortDirection { Ascending, Descending }
