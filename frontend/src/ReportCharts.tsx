import { Alert, Col, Empty, Row, Spin } from 'antd';
import { Pie, Column, Bar, Line } from '@ant-design/plots';
import { useQuery } from '@tanstack/react-query';
import { api } from './api';

export function ReportCharts(){
  const {data,isLoading,error}=useQuery({queryKey:['reports'],queryFn:api.reports});
  if(isLoading)return <Spin/>;if(error)return <Alert type="error" message={error.message}/>;
  const charts=[{title:'合同状态分布',data:data?.byStatus,render:(rows:any[])=> <Pie data={rows} angleField="value" colorField="name" innerRadius={0.6} height={280}/>},{title:'合同类型统计',data:data?.byType,render:(rows:any[])=> <Column data={rows} xField="name" yField="value" height={280}/>},{title:'部门合同数量',data:data?.byDepartment,render:(rows:any[])=> <Bar data={rows} xField="name" yField="value" height={280}/>},{title:'月度新增合同',data:data?.monthly,render:(rows:any[])=> <Line data={rows} xField="month" yField="count" height={280}/>}];
  return <Row gutter={[24,24]} className="section-gap">{charts.map(chart=><Col xs={24} lg={12} key={chart.title}><section><h3>{chart.title}</h3>{chart.data?.length?chart.render(chart.data):<Empty/>}</section></Col>)}</Row>;
}
