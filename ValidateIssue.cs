using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Octokit;
using GraphQL.Client.Http;
using GraphQL.Client.Serializer.Newtonsoft;
using GraphQL;
using System.Collections.Generic;

namespace ValidateIssuesGithub
{
    public static class ValidateIssue
    {
        #region Constants
        private const  int    BAD_JSON_INT_VALUE             = -1;
        private const  string BAD_JSON_STIRNG_VALUE          = "Invalid JSON parsing";
        private const  string GITHUB_APP_PRIVATE_KEY         = @"
MIIEowIBAAKCAQEAzJI7p1Wz/aqSuN6tvAtI38nnql9sj1Op7zGzvUGZYikNhTsV
HxnuIAZHy/V6zPP8+nlM40n4tehq+VL+h0d09ZstSjcobdcg7ghEOg/JmIGFi3nC
00PSKeX1ykhmHUp4IzudHROyqGcnpjNS14fF+B6iLxY5gHI/fuADAHKWqsDaRYjg
rZC2esDMa5DxUs4s/NjiNWyCmuRdkRhjiClgRDKLJjRl3hE2cbq3XYmViVEF/fsc
HB8J4nuhFpf1+R9ezHkY9IrlRuiu2Q8YGKL+gAMEsNTnA6yTIZd5oy8eSD9p6sKc
htqWeRxsFoCEdxjvn+3wj4YP1iV29O0wBFt4gQIDAQABAoIBAFfg5cFffp+Uu8yw
09841chU2rEEpwT3AsQfDMBbQsG5Mvatx8gBgpq9N/B09pi+o0kR/KaS60VxnyqV
rYN9fc/YJl+ATFzLEnlOkciDaa2azjx5ROkudETNZYXNDhi9GdjAziBkitXu4khy
Ob8eszuAJVmm6XK1IXOmVYPGtdSJmUs5Bd6ahbQ/vaJzVOzHayjsSGOMbXicAfiu
bh88AQJDqHH9LYE12fiEZhs8KogTs4MnKhc090NNVoF5G2dAeZ47itexSX6G0P62
LL3xGLNViLPnpxE5Eir1MQnEgTgCLcKkUFOlg23/XroP6Ft0/M+2a3BZF+ZaSOz6
EJ8qe8ECgYEA6vqobPVGzOQJtCtbzkpFm5sia+Z3ZPC21e/v8+7CjHms3ZQSruzq
AMRk81d67gwgxbVJwvDISsMih0Njh4lHDhg/k4j3N+3FbkE78S3HpCE71n8SW1Qo
PKw/Eh9tswLFCRClQOXaeqbKpts5t5EQzSAExfgzO7ItatViyiJjkskCgYEA3t8x
TG6DJF56oE9nDhBAHTRJ0GoHjoBmRBHyK3CbyvWN2KhQOMCn8vmv35RMmVskO9IO
n7OeH6mFu13stKIFrqhEBenl4wSM2wt6stnj9Vmstbuw5WQPgVBxJZuVdCREI8Gy
54u99tpqBkC8XLZ6/0srFyk+8uFlat8kk4mSm/kCgYA02l6J632aVmyMVvhWZURU
5McQSA1w6efmJQru7jRaToAAcu7k46sasxIV3gZrhtTUQ5usumYC0vNwQ0se0FTo
KbIbKEKbFONEkm2+KNLv6v2/mGNzoXFPfFrPY7xT+HqDOHhDKbBDyEJq14Ka9Ik3
6kzIjrRPaBtpHUgUOTn2aQKBgQDUsK0sYr62Y4+lE4Gmsy1scW0L/1Ps025FAddZ
S2LyIrrWi3HbZ0ggIdaMiMs9AvSmPgWEtPZvAunD8JOnooPHtX8NIbUonDwMAn16
12OrzoN6/36Gu6HsZ6dDG6JaLw30DbM9M2f7f171Tqwz0lW48rRRqyQOx7lwbzlJ
r12BiQKBgEqnGNw/dX+tEpza3ke6Rg9EeC6YKXFIZNh6QPaUXXn5iT/JDjfdWP8E
wZPXrnV/oB0NniaWEWX4Tn8pgKuDUfidkeM3Qsz0XQlGsRbWCohCVDN+c0ZzfS2d
lIVxLRDOFE5ocQ+VmUy8FmtrpzR935YBla2Z0f5iPmwne/B9T4wl
";
        private const  int    GITHUB_APP_ID                  = 114533;
        private const  int    GITHUB_APP_JWT_TOKEN_LIFE_TIME = 600;// 10 minutes is the maximum time allowed
        private static bool   DEBUG_LOGS                     = false;
        private static bool   BAD_DATA_COME                  = false;
        #endregion

        [FunctionName("ValidateIssue")]
        public static async Task<IActionResult> Run(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", "post", Route = null)] HttpRequest req,
            ILogger log )
        {
            log.LogInformation( $"Webhook was triggered!" );

            
            var requestedValues = GetDataFromJson( req, log ).Result;
            
            if ( BAD_DATA_COME )
            {
                return new BadRequestObjectResult( "Task complited with error" );
            }

            #region GitHub App auth and new check created

            var generator = new GitHubJwt.GitHubJwtFactory(
            new GitHubJwt.StringPrivateKeySource( GITHUB_APP_PRIVATE_KEY ),
            new GitHubJwt.GitHubJwtFactoryOptions
            {
                AppIntegrationId = GITHUB_APP_ID,
                ExpirationSeconds = GITHUB_APP_JWT_TOKEN_LIFE_TIME
            }
            );
 
            var jwtToken = generator.CreateEncodedJwtToken();

            var appClient = new GitHubClient( new ProductHeaderValue( "TestSimpleApplication" ) )
            {
                Credentials = new Credentials( 
                    jwtToken, 
                    AuthenticationType.Bearer 
                    )
            };

            var responce = await appClient.GitHubApps.CreateInstallationToken( requestedValues.InstallationId );

            var checkClient = new GitHubClient( new ProductHeaderValue("test") )
            {
                Credentials = new Credentials( responce.Token )
            };

            var chRun = await checkClient.Check.Run.Create(
                requestedValues.RepoId, 
                new NewCheckRun(
                    "Losur-check",
                    requestedValues.HeadSHA
                    ) 
                );

            #endregion

            var valuesGraphQL = IsIssueAttached( responce.Token, requestedValues, log ).Result;

            #region Create Update run for Check
            var checkUpdate = new CheckRunUpdate();

           
            if (BAD_DATA_COME)
            {
                checkUpdate.Status = CheckStatus.Completed;
                checkUpdate.Conclusion = CheckConclusion.Failure;
                log.LogError("Complited with error");
            }
            else
            {
                if (valuesGraphQL.LastTag.Equals("ConnectedEvent"))
                {
                    checkUpdate.Status = CheckStatus.Completed;
                    checkUpdate.Conclusion = CheckConclusion.Success;
                        
                }
                else
                {
                    checkUpdate.Status = CheckStatus.Completed;
                    checkUpdate.Conclusion = CheckConclusion.Failure;
                }
                
                log.LogInformation("Complited");
            }

            await checkClient.Check.Run.Update(
                requestedValues.RepoId, 
                chRun.Id, 
                checkUpdate 
            );
            #endregion

            
            return new OkObjectResult( $"Task return {!BAD_DATA_COME}" );
        }

        public static async Task<ValuesOfGraphQLResponce> IsIssueAttached(string token, RequestValues rv, ILogger log)
        {
            var client = new GraphQLHttpClient(
                "https://api.github.com/graphql",
                new NewtonsoftJsonSerializer()
                );

            client.HttpClient.DefaultRequestHeaders.Add(
                "Authorization",
                $"bearer { token }"
                );

            var issueRequest = new GraphQLRequest
            {
                Query = @"
               query {
                  repositoryOwner(login:""" + rv.Owner + @""") {
                    repository(name: """ + rv.RepoName + @""") {
                      pullRequest(number: " + rv.PrNumber + @") {
                        timelineItems(itemTypes: [CONNECTED_EVENT, DISCONNECTED_EVENT], first: 100) {
                       filteredCount
                        nodes {
                          ... on ConnectedEvent {
                            __typename
                            subject {
                              ... on Issue {
                                number
                              }
                            }
                          }
                          ... on DisconnectedEvent {
                            __typename
                            id
                            subject {
                              ... on Issue {
                                number
                              }
                            }
                          }
                        }
                      }
                    }
                  }
                }
              }
                "
            };
            var graphQLResponse = await client.SendQueryAsync<IssueResonse>(issueRequest);
            var valuesGraphQL = new ValuesOfGraphQLResponce()
            {
                Size = graphQLResponse.Data.RepositoryOwner.Repository.PullRequest.TimelineItems.FilteredCount
            };

            if (valuesGraphQL.Size == 0)
            {
                log.LogError($"GraphQL error. Size equals 0");
            }

            IssueResonse.RepositoryOwnerType.RepositoryType.PullRequestType.TimelineItemsType.NodesType[] arr = graphQLResponse.Data.RepositoryOwner.Repository.PullRequest.TimelineItems.Nodes.ToArray();
            valuesGraphQL.LastTag = arr[valuesGraphQL.Size - 1].__Typename;

            #region Debug logs
            if (DEBUG_LOGS)
            {
                log.LogInformation($"\nValues from GraphQL" +
                    $"\nSize: {valuesGraphQL.Size}" +
                    $"\nLast tag:{valuesGraphQL.LastTag}");
            }
            #endregion

            return valuesGraphQL;
        }

        public static async Task<RequestValues> GetDataFromJson(HttpRequest req, ILogger log)
        {
            string requestBody = await new StreamReader(req.Body).ReadToEndAsync();
            dynamic data = JsonConvert.DeserializeObject(requestBody);
            RequestValues rv = new RequestValues();

            rv.Action             = data?.action                                        ?? BAD_JSON_STIRNG_VALUE;
            rv.PrNumber           = data?.check_suite?.pull_requests?[0].number         ?? BAD_JSON_INT_VALUE;
            rv.RepositoryTargetId = data?.check_suite?.pull_requests?[0].head?.repo?.id ?? BAD_JSON_INT_VALUE;
            rv.HeadSHA            = data?.check_suite?.head_sha                         ?? BAD_JSON_STIRNG_VALUE;
            rv.RepoId             = data?.repository?.id                                ?? BAD_JSON_INT_VALUE;
            rv.RepoName           = data?.repository?.name                              ?? BAD_JSON_STIRNG_VALUE;
            rv.Owner              = data?.repository?.owner?.login                      ?? BAD_JSON_STIRNG_VALUE;
            rv.InstallationId     = data?.installation?.id                              ?? BAD_JSON_INT_VALUE;

            if ( ! rv.Action.Equals("requested") )
            {
                log.LogInformation( $"Not a requested action comes: { rv.Action }" );
                BAD_DATA_COME = true;
                return rv;
            }

            if (rv.Action        .Equals(BAD_JSON_STIRNG_VALUE) ||
                rv.PrNumber           == BAD_JSON_INT_VALUE     ||
                rv.RepositoryTargetId == BAD_JSON_INT_VALUE     ||
                rv.HeadSHA       .Equals(BAD_JSON_STIRNG_VALUE) ||
                rv.RepoId             == BAD_JSON_INT_VALUE     ||
                rv.RepoName      .Equals(BAD_JSON_STIRNG_VALUE) ||
                rv.Owner         .Equals(BAD_JSON_STIRNG_VALUE) ||
                rv.InstallationId     == BAD_JSON_INT_VALUE
                )
            {
                log.LogError("One of values comes with error");
                BAD_DATA_COME = true;
                return rv;
            }

            #region Debug log
            if (DEBUG_LOGS)
            {
                log.LogInformation($"\nAction: {rv.Action}" +
                               $"\nPull Request number: {rv.PrNumber}" +
                               $"\nRepository Id: {rv.RepoId}" +
                               $"\nHea SHA: {rv.HeadSHA}" +
                               $"\nRepository Id: {rv.RepoId}" +
                               $"\nRepository name: {rv.RepoName}");
            }
            #endregion

            return rv;
        }
        
        #region Data classes representations
        public class RequestValues
        {
            public string Action             { get; set; }
            public int    PrNumber           { get; set; }
            public int    RepositoryTargetId { get; set; }
            public string HeadSHA            { get; set; }
            public int    RepoId             { get; set; }
            public string RepoName           { get; set; }
            public string Owner              { get; set; }
            public long   InstallationId     { get; set; }

    }

        public class ValuesOfGraphQLResponce
        {
            public string LastTag { get; set; }
            public int Size { get; set; }
        }

        
        public class IssueResonse
        {
            public RepositoryOwnerType RepositoryOwner { get; set; }

            public class RepositoryOwnerType
            {
                public RepositoryType Repository { get; set; }

                public class RepositoryType
                {
                    public PullRequestType PullRequest { get; set; }

                    public class PullRequestType
                    {
                        public TimelineItemsType TimelineItems { get; set; }

                        public class TimelineItemsType
                        {
                            public int FilteredCount { get; set; }

                            public List<NodesType> Nodes { get; set; }

                            public class NodesType
                            {
                                public string __Typename { get; set; }

                                public SubjectType Subject { get; set; }

                                public class SubjectType
                                {
                                    public int Number { get; set; }
                                }
                            }
                        }
                    }
                }
            }
        }
        #endregion
    }
}
