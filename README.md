# StatementParser
Converts bank statements to usable CSV format.

I'm annoyed that banks don't just let you download old transactions in a data-friendly format. All they give you are these nice-looking, useless PDf statements. There *are* websites that offer to convert these documents into a spreadsheet for you, for free even, but personally I don't trust a random "free" website with my account information.

I started this project as an alternative. It parses the PDF statements directly on your computer, so your personal info never leaves your personal machine. I've also made it open-source, so you can see exactly what the code is doing.

# Supported Bank Statements

This program is only a step or two above "bodge" level. I wrote it to support the bank statements that I personally had to parse, and only tested it with those few examples. Because of this, there are currently *many* types of statements from *many* different banks that are not yet supported.

Here are the types of statements I've tried to support:

 - Chase
   - Credit Card Statements (2017)
   - Credit Card Statements (2018)
 - Navy Federal
   - Credit Card Year-End Summarys (2018)
   
The versions tested above were all Engish and American, so versions in other languages or from other reigons may be incompatable.

I may add support for addition statement types in the future. *You* are welcome to add more statement types yourself, by contibuting your code to the project.
