# Beyond Program - Untethered 365

This repository contains the code for participants of the **Beyond Program** in Untethered 365. It is an Azure Function app that integrates with OpenAI's GPT-4o model and Dataverse. The function is designed to generate and execute SQL queries based on user input, leveraging an SQL schema from a Dynamics 365 CRM database.

## Key Features:
- **User Input Processing:** Accepts user queries and SQL schema to generate the corresponding SQL queries.
- **SQL Query Generation:** Uses OpenAI’s GPT-4o to dynamically create SQL queries based on a user’s question.
- **Database Integration:** Executes the generated SQL query on a Dynamics 365 CRM database and returns the results.

## Setup

1. Clone this repository to your local machine.
2. Set up Azure OpenAI credentials and your CRM database connection string in the environment variables.
3. Deploy to Azure Functions.
