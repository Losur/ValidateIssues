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
        private const int BAD_JSON_INT_VALUE = -1;
        private const string GITHUB_APP_PRIVATE_KEY = @"
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
        #endregion

        [FunctionName("ValidateIssue")]
        public static async Task<IActionResult> Run(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", "post", Route = null)] HttpRequest req,
            ILogger log )
        {
            log.LogInformation( $"Webhook was triggered!" );

            #region Get data from JSON
            string requestBody = await new StreamReader( req.Body ).ReadToEndAsync();
            dynamic data = JsonConvert.DeserializeObject( requestBody );
            string action = data?.action;

            if ( ! action.Equals("requested") )
            {
                log.LogInformation(action);
                return new OkObjectResult("Tas is compited");
            }
            int prNumber = data?.check_suite?.pull_requests?[0].number ?? BAD_JSON_INT_VALUE;
            if ( prNumber == BAD_JSON_INT_VALUE )
            {
                log.LogInformation("Not a pull request");
                return new OkObjectResult("It is not a pull request");
            }
            int repositoryTargetId = data?.check_suite?.pull_requests?[0].head?.repo?.id;
            string headSHA = data?.check_suite?.head_sha;
            int repoId = data?.repository?.id;
            string repoName = data?.repository?.name;
            string owner = data?.repository?.owner?.login;
            log.LogInformation( $"\nOwner: { owner }\nRepo: { repoName }\nPr: { prNumber }\nCommit sha: { headSHA }\nRepos id: { repositoryTargetId }" );
            #endregion

            #region GitHub App auth and new check created

            var generator = new GitHubJwt.GitHubJwtFactory(
            new GitHubJwt.StringPrivateKeySource( GITHUB_APP_PRIVATE_KEY ),
            new GitHubJwt.GitHubJwtFactoryOptions
            {
                AppIntegrationId = 114533, // The GitHub App Id
                ExpirationSeconds = 600 // 10 minutes is the maximum time allowed
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

            long installationId = data?.installation?.id;
            var responce = await appClient.GitHubApps.CreateInstallationToken( installationId );

            var checkClient = new GitHubClient( new ProductHeaderValue("test") )
            {
                Credentials = new Credentials( responce.Token )
            };

            var chRun = await checkClient.Check.Run.Create(
                repoId, 
                new NewCheckRun(
                    "Losur-check", 
                    headSHA
                    ) 
                );
            
            #endregion

            #region GraphQl request

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
                  repositoryOwner(login:""" + owner + @""") {
                    repository(name: """ + repoName + @""") {
                      pullRequest(number: " + prNumber + @") {
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
            var graphQLResponse = await client.SendQueryAsync<IssueResonse>( issueRequest );
            #endregion

            #region GraphQL responce data
            int size = graphQLResponse.Data.RepositoryOwner.Repository.PullRequest.TimelineItems.FilteredCount;
            IssueResonse.RepositoryOwnerType.RepositoryType.PullRequestType.TimelineItemsType.NodesType[] arr = graphQLResponse.Data.RepositoryOwner.Repository.PullRequest.TimelineItems.Nodes.ToArray();
            string lastTag = arr[size - 1].__Typename;
            #endregion

            #region Create Update run for Check
            var checkUpdate = new CheckRunUpdate();

            if ( size == 0 )
            {
                checkUpdate.Status = CheckStatus.Completed;
                checkUpdate.Conclusion = CheckConclusion.Failure;
                
            }
            else {
                if ( lastTag.Equals( "ConnectedEvent" ) )
                {
                    checkUpdate.Status = CheckStatus.Completed;
                    checkUpdate.Conclusion = CheckConclusion.Success;
                }
                else
                {
                    checkUpdate.Status = CheckStatus.Completed;
                    checkUpdate.Conclusion = CheckConclusion.Failure;
                    //await githubClient.Issue.Comment.Create(owner, repoName, prNumber, $"Issue is NOT implement");
                    //responseMessage = "Issue is NOT implement";
                }
            }
            

            await checkClient.Check.Run.Update(
                repoId, 
                chRun.Id, 
                checkUpdate 
            );
            #endregion
            log.LogInformation("Complited");
            return new OkObjectResult( "Task compited sucñessfull" );
        }

        public static ValuesOfGraphQLResponce IsIssueAttached(string token)
        {

            ValuesOfGraphQLResponce values = new ValuesOfGraphQLResponce()
            {
                
            };
            return values;
        }

        public static async Task<RequestValues> GetDataFromJson(HttpRequest req)
        {
            string requestBody = await new StreamReader(req.Body).ReadToEndAsync();
            dynamic data = JsonConvert.DeserializeObject(requestBody);

            string action = data?.action;
            int prNumber = data?.check_suite?.pull_requests?[0].number ?? BAD_JSON_INT_VALUE;
            int repositoryTargetId = data?.check_suite?.pull_requests?[0].head?.repo?.id ?? BAD_JSON_INT_VALUE;
            string headSHA = data?.check_suite?.head_sha;
            int repoId = data?.repository?.id;
            string repoName = data?.repository?.name;
            string owner = data?.repository?.owner?.login;

            if ( !action.Equals("requested") || 
                prNumber == BAD_JSON_INT_VALUE ||
                repositoryTargetId == BAD_JSON_INT_VALUE

                )

            return new RequestValues()
            {
                

            };
        }

        private class RequestValues
        {
            public string Action             { get; set; }
            public int    PrNumber           { get; set; }
            public int    RepositoryTargetId { get; set; }
            public string HeadSHA            { get; set; }
            public int    RepoId             { get; set; }
            public string RepoName           { get; set; }
            public string Owner              { get; set; }



    }

        private class ValuesOfGraphQLResponce
        {
            public string LastTag { get; set; }
            public int Size { get; set; }
        }

        #region GraphQL responce object class
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
