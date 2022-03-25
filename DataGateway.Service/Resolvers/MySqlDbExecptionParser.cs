using System;
using System.Data.Common;
using System.Net;
using Azure.DataGateway.Service.Exceptions;

namespace Azure.DataGateway.Service.Resolvers
{
    public class MySqlDbExceptionParser : IDbExceptionParser
    {
        public Exception Parse(DbException e)
        {
            // refer to https://dev.mysql.com/doc/connector-odbc/en/connector-odbc-reference-errorcodes.html
            // for error codes
            switch (e.SqlState)
            {
                case "23000":
                    return new DataGatewayException(
                        message: $"MySql Error {e.SqlState}: Integrity Contraint Violation.",
                        statusCode: HttpStatusCode.InternalServerError,
                        subStatusCode: DataGatewayException.SubStatusCodes.DatabaseOperationFailed
                    );
                default:
                    return e;
            }
        }
    }
}
