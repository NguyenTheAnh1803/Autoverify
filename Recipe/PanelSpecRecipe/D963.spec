{
    "PanelSpecRecipe": {
        "cereal_class_version": 0,
        "Specs": [
            {
                "Name": "SR_OVER_TOP",
                "ShapeCode": "",
                "ConnectionCode": "",
                "Judge": "NG",
                "ReasonCode": "",
                "Script": "#SR_OVERFLOW_TOP\r\narr1 = list()\r\narr1.append('type_SR_OverFlow_Top')\r\n\r\n#SR_Resin_Check_TOP\r\narr2 = list()\r\narr2.append('type_SR_Resin_Check_Top')\r\n\r\n#SR_OVERFLOW_TOP 후보점추출,불량갯수\r\ndfts1 = Spec.getReferenceDefects(arr1)\r\ncnt1= len(dfts1)\r\n\r\n#SR_Resin_Check_TOP 후보점추출,불량갯수\r\ndfts2 = Spec.getReferenceDefects(arr2)\r\ncnt2= len(dfts2)\r\n\r\n\r\n#조건 \r\nif cnt1 >= 1 and cnt2 >= 1: \r\n\tSpec.countExecuteJudge(dfts1)\r\n\tSpec.countExecuteJudge(dfts2)\r\n\t\r\nreturn\r\n\r\n"
            },
            {
                "Name": "SR_OVER_BOT",
                "ShapeCode": "",
                "ConnectionCode": "",
                "Judge": "NG",
                "ReasonCode": "",
                "Script": "#SR_OVERFLOW_TOP\r\narr1 = list()\r\narr1.append('type_SR_OverFlow_Bot')\r\n\r\n#SR_Resin_Check_TOP\r\narr2 = list()\r\narr2.append('type_SR_Resin_Check_Bot')\r\n\r\n#SR_OVERFLOW_TOP 후보점추출,불량갯수\r\ndfts1 = Spec.getReferenceDefects(arr1)\r\ncnt1= len(dfts1)\r\n\r\n#SR_Resin_Check_TOP 후보점추출,불량갯수\r\ndfts2 = Spec.getReferenceDefects(arr2)\r\ncnt2= len(dfts2)\r\n\r\n\r\n#조건 \r\nif cnt1 >= 1 and cnt2 >= 1: \r\n\tSpec.countExecuteJudge(dfts1)\r\n\tSpec.countExecuteJudge(dfts2)\r\n\t\r\nreturn\r\n\r\n"
            },
            {
                "Name": "MCL_Width_NG_UCL_SPEC",
                "ShapeCode": "",
                "ConnectionCode": "",
                "Judge": "NG",
                "ReasonCode": "",
                "Script": "#Defect Type\r\narr1 = list()\r\narr1.append('type_MCL_WIDTH_SPEC_OVER')\r\n\r\n#MCL_WIDTH_SPEC_OVER 후보점추출,불량갯수\r\ndfts1 = Spec.getReferenceDefects(arr1)\r\ncnt1= len(dfts1)\r\n\r\n#조건 \r\nif cnt1 >= 1:\r\n\tSpec.countExecuteJudge(dfts1)\r\n\t\r\nreturn\r\n"
            },
            {
                "Name": "MCL_Width_NG_LCL_SPEC",
                "ShapeCode": "",
                "ConnectionCode": "",
                "Judge": "NG",
                "ReasonCode": "",
                "Script": "#Defect Type\r\narr1 = list()\r\narr1.append('type_MCL_WIDTH_SPEC_DOWN')\r\n\r\n#MCL_WIDTH_SPEC_DOWN 후보점추출,불량갯수\r\ndfts1 = Spec.getReferenceDefects(arr1)\r\ncnt1= len(dfts1)\r\n\r\n#조건 \r\nif cnt1 >= 1:\r\n\tSpec.countExecuteJudge(dfts1)\r\n\t\r\nreturn\r\n"
            }
        ]
    }
}