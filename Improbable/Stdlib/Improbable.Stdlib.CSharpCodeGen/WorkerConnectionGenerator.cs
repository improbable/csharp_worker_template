using System.Collections.Generic;
using System.Text;
using Improbable.CSharpCodeGen;
using Improbable.Schema.Bundle;

namespace Improbable.Stdlib.CSharpCodeGen
{
    public class StdlibGenerator : ICodeGenerator
    {
        private const string WorkerConnectionType = "global::Improbable.Stdlib.WorkerConnection";
        private const string CancellationTokenType = "global::System.Threading.CancellationToken";
        private readonly Bundle bundle;

        public StdlibGenerator(Bundle bundle)
        {
            this.bundle = bundle;
        }

        public string Generate(TypeDescription type)
        {
            if (!type.ComponentId.HasValue)
            {
                return string.Empty;
            }

            var sb = new StringBuilder();

            sb.AppendLine(GenerateCommands(type, bundle.Components[type.QualifiedName].Commands));
            sb.AppendLine(GenerateUpdate(type));
            sb.AppendLine(GenerateComponentCollection(type));

            return sb.ToString();
        }

        private static string GenerateComponentCollection(TypeDescription type)
        {
            var name = Case.CapitalizeNamespace(type.QualifiedName);
            var collectionType = $"global::Improbable.Stdlib.ComponentCollection<global::{name}>";

            return $@"public static {collectionType} CreateComponentCollection()
            {{
                return new {collectionType}(ComponentId, Create, ApplyUpdate);
            }}";
        }

        private static string GenerateCommands(TypeDescription type, IReadOnlyList<ComponentDefinition.CommandDefinition> commands)
        {
            var componentName = Case.CapitalizeNamespace(type.QualifiedName);
            var text = new StringBuilder();
            var bindingMethods = new StringBuilder();

            var commandIndices = new StringBuilder();
            foreach (var cmd in commands)
            {
                var response = Case.CapitalizeNamespace(cmd.ResponseType);
                var request = Case.CapitalizeNamespace(cmd.RequestType);
                var cmdName = Case.SnakeCaseToPascalCase(cmd.Name);

                commandIndices.AppendLine($"{cmdName} = {cmd.CommandIndex},");

                bindingMethods.AppendLine($@"public global::System.Threading.Tasks.Task<{response}> Send{cmdName}Async({request} request, {CancellationTokenType} cancellation = default, uint? timeout = null, global::Improbable.Worker.CInterop.CommandParameters? parameters = null)
{{
    return global::{Case.CapitalizeNamespace(type.QualifiedName)}.Send{cmdName}Async(connection, entityId, request, cancellation, timeout, parameters);
}}

public void Send{cmdName}Response(uint id, {response} response)
{{
    global::{Case.CapitalizeNamespace(type.QualifiedName)}.Send{cmdName}Response(connection, id, response);
}}
");

                text.AppendLine($@"public static void Send{cmdName}Response({WorkerConnectionType} connection, uint id, {response} response)
{{
    var schemaResponse = new global::Improbable.Worker.CInterop.SchemaCommandResponse({componentName}.ComponentId, {cmd.CommandIndex});
    response.ApplyToSchemaObject(schemaResponse.GetObject());

    connection.SendCommandResponse(id, schemaResponse);
}}

public static global::System.Threading.Tasks.Task<{response}> Send{cmdName}Async({WorkerConnectionType} connection,
                                                                                 {Types.EntityIdType} entityId,
                                                                                 {request} request,
                                                                                 {CancellationTokenType} cancellation = default,
                                                                                 uint? timeout = null,
                                                                                 global::Improbable.Worker.CInterop.CommandParameters? parameters = null,
                                                                                 global::System.Threading.Tasks.TaskCreationOptions options = global::System.Threading.Tasks.TaskCreationOptions.None)
{{
    var schemaRequest = new global::Improbable.Worker.CInterop.SchemaCommandRequest({componentName}.ComponentId, {cmd.CommandIndex});
    request.ApplyToSchemaObject(schemaRequest.GetObject());

    var completion = new global::System.Threading.Tasks.TaskCompletionSource<{response}>(options);
    if (cancellation.CanBeCanceled)
    {{
        cancellation.Register(() => completion.TrySetCanceled(cancellation));
    }}    

    void Complete(global::Improbable.Stdlib.WorkerConnection.CommandResponses r)
    {{
        var result = new {response}(r.UserCommand.Response.SchemaData.Value.GetObject());
        completion.TrySetResult(result);
    }}

    void Fail(global::Improbable.Worker.CInterop.StatusCode code, string message)
    {{
        completion.TrySetException(new global::Improbable.Stdlib.CommandFailedException(code, message));
    }}

    connection.Send(entityId, schemaRequest, timeout, cancellation, parameters, Complete, Fail);

    return completion.Task;
}}");
            }

            if (commandIndices.Length > 0)
            {
                text.Append($@"
public enum Commands
{{
{Case.Indent(1, commandIndices.ToString().TrimEnd())}
}}

public static Commands? GetCommandType(global::Improbable.Worker.CInterop.CommandRequestOp request)
{{
    if (request.Request.ComponentId != ComponentId)
    {{
        throw new global::System.InvalidOperationException($""Mismatch of ComponentId (expected {{ComponentId}} but got {{request.Request.ComponentId}}"");
    }}

    if (!request.Request.SchemaData.HasValue)
    {{
        return null;
    }}

    return (Commands)request.Request.SchemaData.Value.GetCommandIndex();
}}

public readonly struct CommandSenderBinding
{{
    private readonly {WorkerConnectionType} connection;
    private readonly {Types.EntityIdType} entityId;

    public CommandSenderBinding({WorkerConnectionType} connection, {Types.EntityIdType} entityId)
    {{
        this.connection = connection;
        this.entityId = entityId;
    }}
{Case.Indent(1, bindingMethods.ToString().TrimEnd())}
}}

public static CommandSenderBinding Bind({WorkerConnectionType} connection, {Types.EntityIdType} entityId)
{{
    return new CommandSenderBinding(connection, entityId);
}}
");
            }

            return text.ToString();
        }

        public static string GenerateUpdate(TypeDescription type)
        {
            var typeName = Case.GetPascalCaseNameFromTypeName(type.QualifiedName);
            var typeNamespace = Case.GetPascalCaseNamespaceFromTypeName(type.QualifiedName);

            var update = $"global::{typeNamespace}.{typeName}.Update";

            return $@"public static void SendUpdate({WorkerConnectionType} connection, {Types.EntityIdType} entityId, {update} update, global::Improbable.Worker.CInterop.UpdateParameters? updateParams = null)
{{
    connection.SendComponentUpdate(entityId.Value, update.ToSchemaUpdate(), updateParams);
}}
";
        }
    }
}
