using Flurl;
using Flurl.Http;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using TuneLab.Foundation.Utils;

namespace TuneLab.Base.Utils;

public class ApiResponse<T>
{
    public bool IsSuccessful { get; set; }
    public T Content { get; set; }
    public string ErrorMessage { get; set; }
}

public class HttpClient
{
    private readonly string mBaseUrl;

    public HttpClient(string baseUrl)
    {
        mBaseUrl = baseUrl;
    }

    // Send GET request
    public async Task<ApiResponse<string>> GetAsync(string path, Dictionary<string, object> queryParams = null)
    {
        var response = new ApiResponse<string>
        {
            IsSuccessful = false,
            Content = null,
            ErrorMessage = null
        };

        try
        {
            var url = mBaseUrl.AppendPathSegment(path);
            if (queryParams != null)
            {
                url = url.SetQueryParams(queryParams);
            }

            var result = await url.GetStringAsync();
            response.IsSuccessful = true;
            response.Content = result;
        }
        catch (FlurlHttpTimeoutException e)
        {
            Log.Error("Request Timeout: " + e.Message);
            response.ErrorMessage = "Request Timeout:" + e.Message;
        }
        catch (FlurlHttpException e)
        {
            Log.Error("HTTP Error" + e.Message);
            response.ErrorMessage = "HTTP Error: " + e.Message;
        }
        catch (Exception e)
        {
            Log.Error("Request Error: " + e.Message);
            response.ErrorMessage = "Request Error: " + e.Message;
        }

        return response;
    }

    // Send POST request
    public async Task<ApiResponse<string>> PostAsync<T>(string path, T data)
    {
        var response = new ApiResponse<string>
        {
            IsSuccessful = false,
            Content = null,
            ErrorMessage = null
        };

        try
        {
            var result = await mBaseUrl.AppendPathSegment(path).PostJsonAsync(data).ReceiveString();
            response.IsSuccessful = true;
            response.Content = result;
        }
        catch (FlurlHttpTimeoutException e)
        {
            Log.Error("Request Timeout: " + e.Message);
            response.ErrorMessage = "Request Timeout:" + e.Message;
        }
        catch (FlurlHttpException e)
        {
            Log.Error("HTTP Error: " + e.Message);
            response.ErrorMessage = "HTTP Error: " + e.Message;
        }
        catch (Exception e)
        {
            Log.Error("Request Error: " + e.Message);
            response.ErrorMessage = "Request Error: " + e.Message;
        }

        return response;
    }
}
