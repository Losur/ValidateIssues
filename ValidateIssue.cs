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
using System.Resources;
using System.Reflection;

namespace ValidateIssuesGithub
{
    public static class ValidateIssue
    {
        #region Constants
        private const  int    BAD_JSON_INT_VALUE             = -1;
        private const  string BAD_JSON_STIRNG_VALUE          = "Invalid JSON parsing";
        private const  int    GITHUB_APP_ID                  = 114533;
        private const  int    GITHUB_APP_JWT_TOKEN_LIFE_TIME = 600;// 10 minutes is the maximum time allowed
        private static bool   DEBUG_LOGS                     = false;
        private static bool   BAD_DATA_COME                  = false;

        private const string GITHUB_APP_PRIVATE_KEY = @"MIIEpAIBAAKCAQEAs8ZOZqm6T/T+VjKO2rY+rLC/qbHPVy5g3628nTTM+erWlmC8
OV+2dyMCPUMb9oRlNTYKgYJUMGi+IPDkVaGwP6Hsz6fcZ9vIlH6eHrTBVzLYGy/B
gSJe+1Am17cIIPpH0KaJjmNYFtb7oHtx4q2CkEbX4PwgJGIc2x32jLsTlELeUT7U
ztQvrPANn5cJu57wouiSM0FR/SAELLkoqVHQA8jusgYuXIs5gjWWQVfz1kV2mdO8
dGuU70X6+RIs0rrAMK608PY3AUbE97/4wykkcyPQKG/E2ZNYmq/8ZBumCfIHedzu
sQ+epbgTY5FaHeVs5153EzP/Bh2JkPZR/kaWVwIDAQABAoIBAQCfqB9ax6PCfOcf
8Fi0XqP8xCADefmVCIhaPjbDOvBLh8c52AFxxtIKrlm/xIjh/yTPBAaCjBduwqcQ
JD/02NrpOEpTBVYWGrfhQS32QTtv0KTiSCBHKhpGgSFt9IxQlVYQNMb3YL0L07O3
C8rRsJzCu1ff5Ko7BbNw2gRraX1y7gz1RhDfiPLdvnOCpnw+dCS5G5+DuoH9ytYo
C0Bck+Nz5dNgXu+UJVwXhNTaSF2c61xheSbLWHmQazr42cfBjCev0E3nuc7qKTT6
W1UmFU/6mJfLUc+GnFc8Tjv/s36CIa8CWJgtWzOjDn+JDFaN+7mgtCHkUcfHbs8v
KQKEUf6BAoGBAO11nD6TScbZgJQqo/8NcfQO0nnv761/qIO0SMtOUdZg21hNiGmC
ZozNV61t3GpUlzxdyyHfSIOAq4l8+v0rzhpigWPxc06f/nUPsHmWVZ2/RETREDeX
dL5XzPKhrqG93+fCWcuZZAfrZPl7DqKgCdFKbUNeVslz0WIs9j5BZ61BAoGBAMHP
rh5pxMQCroCbseSibigsneoiqkRhHrCJZJjgFwbqiGRuJhdsQ0KS3PdyJSGMlZYZ
piPeGvC0Oxj1Mnm8u2gVMY/lIqr+/+1U3MNvuVDbmxg+kElm/w21JXqfZ1Qn98gA
/B8ctGu5OpPY2BUndFgOYMGFpzNts0EHbkWH1SWXAoGAdZGFinXiUVHfF30FNYKy
qOOt0jG5uW07QfpBEGf2nO3XrCC3KYYmwA/rGTMLrpmzR3Ao4txqSrGqPKhknHTT
1rxu08z4CjWtBsh917VXLoNEic34+Y1Df/p4vqjOjcY01cqkKuoHXORvWhZTaLFU
Kwtujaxny9ZMFQ+t26UGcAECgYBXUE3cK8BWofKlw/7Xxwmjlb4q3iUhGzPtSmiE
qugU2JJL1Ifao46Fro5X+BecTq6Racq8e/JdIIVDUCvGRm2TjYC/l/YPXURFUqcG
cQ3mzJjJyl3Mg9dCAKr63Fd7xWnOtArhpVfu9Arc0qM+nIDArvGOHb1e4PwRvtxB
/NjczwKBgQCn1CntFaWJmkFXuogMp9Ww0a64PN7gdu93MXtk1T6xEfAVRnwqkNmw
FUloqx8/ccfaNlqtkzqmGo2xKAk4u5CHq8E7eggoeLdyIqa6eF0EQVslnibkvYdw
sGzmRq9ENrFnH4l2mhXOiCDxQceHmS5HqviCH1kly9fb2+H/R8uvQg==";
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
            //System.Resources.ResourceManager rm = new System.Resources.ResourceManager("Github", Assembly.GetExecutingAssembly());
           // Console.WriteLine(rm);
          //  Console.WriteLine(rm.GetString("s"));
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

            var valuesGraphQL = IsIssueAttached( responce, requestedValues, log ).Result;
            Console.WriteLine("wmth");
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

        public static async Task<ValuesOfGraphQLResponce> IsIssueAttached(AccessToken responce, RequestValues rv, ILogger log)
        {
            var client = new GraphQLHttpClient(
                "https://api.github.com/graphql",
                new NewtonsoftJsonSerializer()
                );

            client.HttpClient.DefaultRequestHeaders.Add(
                "Authorization",
                $"bearer { responce.Token }"
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
