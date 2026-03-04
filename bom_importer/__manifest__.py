# -*- coding: utf-8 -*-
{
    'name': "BOM Importer",

    'summary': "Custom Bom Import",

    'description': """
    This is an example of how odoo can take the expected Json tree from the SWOdooBomImport app.
    """,

    'author': "Tanner Troumbley",

    # Categories can be used to filter modules in modules listing
    # Check https://github.com/odoo/odoo/blob/15.0/odoo/addons/base/data/ir_module_category_data.xml
    # for the full list
    'category': 'Uncategorized',
    'version': '0.1',

    # any module necessary for this one to work correctly
    'depends': ['mrp'],

    # always loaded
    'data': [
        'security/ir.model.access.csv',
        'views/bom_importer.xml',
    ],
}

