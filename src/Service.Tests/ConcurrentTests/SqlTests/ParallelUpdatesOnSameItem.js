import { generateEasyAuthToken, validateResposneBodies, validateStatusCode, validateNoErrorsInResponse } from '../Helper.js';
import { check } from 'k6';
import http from 'k6/http';

export const validateParallelUpdateOperationsOnSameItem = () => {

    let accessToken = generateEasyAuthToken();

    let headers = {
        'X-MS-CLIENT-PRINCIPAL': accessToken,
        'X-MS-API-ROLE': 'authenticated',
        'content-type': 'application/json'
    };

    const parameters = {
        headers: headers
    }

    let updateNoteBookMutation = `mutation updateNotebook($id: Int!, $item: UpdateNotebookInput!) {
        updateNotebook(id: $id, item: $item) {
          id
          color
        }
      }
      `;    

    let updateColorToCyanVariable = {
        "id": 3,
        "item": {
            "color": "cyan"
        }
      };

    let updateColorToMagentaVariable = {
        "id": 3,
        "item": {
            "color": "magenta"
        }
      };

    // Each REST or GraphQL request is created as a named request. Named requests are useful
    // for validating the responses.
    const queryNames = ['updateNotebookColorToCyan', 'updateNotebookColorToMagenta'];

    // Expected status codes for each request
    const expectedStatusCodes = {
        'updateNotebookColorToCyan': 200,
        'updateNotebookColorToMagenta': 200
    };

    // Expected response when the mutation that updates color to cyan runs last
    const expectedResponse1 = {
        "data": {
          "updateNotebook": {
            "id": 3,
            "color": "cyan"
          }
        }
      };
    
    // Expected response when the mutation that updates color to magenta runs last
    const expectedResponse2 = {
        "data": {
          "updateNotebook": {
            "id": 3,
            "color": "magenta"
          }
        }
      };
     
    
    const requests = {
        'updateNotebookColorToCyan': {
            method: 'POST',
            url: 'https://localhost:5001/graphql/',
            body: JSON.stringify({ query: updateNoteBookMutation, variables: updateColorToCyanVariable }),
            params: parameters
        },
        'updateNotebookColorToMagenta': {
            method: 'POST',
            url: 'https://localhost:5001/graphql/',
            body: JSON.stringify({ query: updateNoteBookMutation, variables: updateColorToMagentaVariable }),
            params: parameters
        }
    };

    // Performs all the GraphQL and REST requests in parallel
    const responses = http.batch(requests);

    // Validations for the API responses
    check(responses, {
        'Validate no errors': validateNoErrorsInResponse(queryNames, responses),
        'Validate expected status code': validateStatusCode(queryNames, responses, expectedStatusCodes),
        'Validate API response': validateResposneBodies(queryNames, responses, expectedResponse1, expectedResponse2)
    });
};
